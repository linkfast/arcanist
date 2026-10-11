using System.Text.Json;
using Arcanist.Core.Api;
using Arcanist.Core.Application;
using LibraryRules = Arcanist.Core.Application.Libraries.LibraryRules;
using Arcanist.Core.Application.Users;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Arcanist.Core.Scanning;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Setup;

public sealed class SetupOptions
{
    public const string Section = "Arcanist:Setup";
    public const string CompletedKey = Section + ":Completed";

    public bool Completed { get; set; }
}

public sealed record SetupStatus(bool Completed, bool RequiresCode, SetupDatabaseStatus Database, string DataDirectory);

/// <param name="LockedByEnvironment">The database is set outside the wizard (environment variables, appsettings),
/// so the wizard skips that step and ignores <see cref="SetupRequest.Database"/>.</param>
public sealed record SetupDatabaseStatus(bool Configured, bool LockedByEnvironment, DatabaseProvider? Provider, string DefaultSqlitePath);

public sealed record SetupDatabase(DatabaseProvider? Provider, string? ConnectionString);

public sealed record SetupAdmin(string? UserName, string? Password, string? DisplayName);

public sealed record SetupLibrary(string? Name, IReadOnlyList<string>? Folders, bool VisibleToAll = true);

/// <summary>Everything the wizard collects, submitted at once so setup never ends half-done.</summary>
public sealed record SetupRequest(
    SetupDatabase? Database,
    SetupAdmin? Admin,
    IReadOnlyList<SetupLibrary>? Libraries,
    bool? ShareAnonymousStatistics,
    string? Language);

public sealed record DatabaseTestResult(bool Success, string? Error);

public abstract record SetupResult
{
    public sealed record Completed : SetupResult;

    public sealed record AlreadyCompleted : SetupResult;

    public sealed record Invalid(ValidationErrors Errors) : SetupResult;
}

public sealed class SetupService(
    IOptionsMonitor<SetupOptions> setupOptions,
    IOptionsMonitor<DatabaseOptions> databaseOptions,
    ISettingsFileWriter settingsWriter,
    DatabaseInitializer initializer,
    ArcanistPaths paths,
    SetupCode setupCode,
    IPasswordHasher<User> passwordHasher,
    LibraryRules libraryRules,
    ScanCoordinator scans,
    ILogger<SetupService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsCompleted => setupOptions.CurrentValue.Completed;

    private bool IsDatabaseLockedByEnvironment =>
        settingsWriter.IsSetOutsideSettingsFile(DatabaseOptions.Section + ":" + nameof(DatabaseOptions.Provider));

    public SetupStatus GetStatus(bool isLocalRequest)
    {
        var database = databaseOptions.CurrentValue;
        return new SetupStatus(
            IsCompleted,
            RequiresCode: !IsCompleted && !isLocalRequest,
            new SetupDatabaseStatus(
                database.Provider is not null,
                IsDatabaseLockedByEnvironment,
                database.Provider,
                Path.Combine(paths.Root, "arcanist.db")),
            paths.Root);
    }

    public async Task<DatabaseTestResult> TestDatabaseAsync(SetupDatabase request, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var options = ValidateDatabase(request, "", errors);
        if (options is null)
            return new DatabaseTestResult(false, ErrorCodes.InvalidFormat);

        var error = await initializer.TestConnectionAsync(options, cancellationToken);
        return new DatabaseTestResult(error is null, error);
    }

    public async Task<SetupResult> CompleteAsync(SetupRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsCompleted)
                return new SetupResult.AlreadyCompleted();

            var errors = new ValidationErrors();
            var lockedDatabase = IsDatabaseLockedByEnvironment;

            var database = lockedDatabase
                ? databaseOptions.CurrentValue
                : request.Database is null
                    ? Required(errors, "database")
                    : ValidateDatabase(request.Database, "database", errors);

            if (request.Admin is null)
                errors.Add("admin", ErrorCodes.Required);
            else
            {
                UserRules.ValidateUserName(request.Admin.UserName, "admin.userName", errors);
                UserRules.ValidatePassword(request.Admin.Password, "admin.password", errors);
            }

            var libraries = request.Libraries ?? [];
            var folders = await ValidateLibrariesAsync(libraries, errors, cancellationToken);

            if (request.ShareAnonymousStatistics is null)
                errors.Add("shareAnonymousStatistics", ErrorCodes.Required);

            if (!errors.IsEmpty || database is null)
                return new SetupResult.Invalid(errors);

            if (await initializer.TestConnectionAsync(database, cancellationToken) is not null)
            {
                errors.Add("database", ErrorCodes.ConnectionFailed);
                return new SetupResult.Invalid(errors);
            }

            if (!lockedDatabase)
            {
                var values = new Dictionary<string, object?> { [DatabaseOptions.Section + ":Provider"] = database.Provider };
                if (database.ConnectionString is not null)
                    values[DatabaseOptions.Section + ":ConnectionString"] = database.ConnectionString;

                await settingsWriter.SetAsync(values, cancellationToken);
            }

            await initializer.MigrateAsync(database, cancellationToken);

            IReadOnlyList<Guid> libraryIds;
            await using (var db = ArcanistDbContextFactory.Create(database, paths))
            {
                if (await db.Users.AnyAsync(cancellationToken))
                {
                    // A database that already has accounts (for example a reused PostgreSQL) is not taken over.
                    errors.Add("database", ErrorCodes.DatabaseNotEmpty);
                    return new SetupResult.Invalid(errors);
                }

                libraryIds = Seed(db, request, libraries, folders);
                await db.SaveChangesAsync(cancellationToken);
            }

            await settingsWriter.SetAsync(new Dictionary<string, object?> { [SetupOptions.CompletedKey] = true }, cancellationToken);
            setupCode.Discard();

            // The wizard's last step shows this first scan in progress.
            foreach (var libraryId in libraryIds)
                await scans.EnqueueAsync(libraryId, ScanKind.Full, requestedBy: null, cancellationToken);

            logger.LogInformation("Setup completed: {Provider} database, administrator {UserName}, {LibraryCount} library(ies)",
                database.Provider, request.Admin!.UserName, libraries.Count);

            return new SetupResult.Completed();
        }
        finally
        {
            _gate.Release();
        }
    }

    private IReadOnlyList<Guid> Seed(ArcanistDbContext db, SetupRequest request, IReadOnlyList<SetupLibrary> libraries, IReadOnlyList<IReadOnlyList<string?>> folders)
    {
        var admin = new User
        {
            UserName = request.Admin!.UserName!.Trim(),
            NormalizedUserName = TextNormalizer.Normalize(request.Admin.UserName),
            DisplayName = string.IsNullOrWhiteSpace(request.Admin.DisplayName) ? null : request.Admin.DisplayName.Trim(),
            Role = UserRole.Admin,
            PasswordHash = "",
        };
        admin.PasswordHash = passwordHasher.HashPassword(admin, request.Admin.Password!);
        db.Users.Add(admin);

        var libraryIds = new List<Guid>();
        for (var i = 0; i < libraries.Count; i++)
        {
            var library = new Library { Name = libraries[i].Name!.Trim(), VisibleToAll = libraries[i].VisibleToAll };
            foreach (var folder in folders[i])
            {
                library.Sources.Add(new LibrarySource
                {
                    Library = library,
                    Kind = LibrarySourceKind.Local,
                    Location = folder!,
                });
            }

            db.Libraries.Add(library);
            libraryIds.Add(library.Id);
        }

        db.Settings.Add(new Setting
        {
            Key = SettingKeys.ShareAnonymousStatistics,
            Value = JsonSerializer.Serialize(request.ShareAnonymousStatistics!.Value),
        });

        if (!string.IsNullOrWhiteSpace(request.Language))
            db.Settings.Add(new Setting { Key = SettingKeys.DefaultLanguage, Value = JsonSerializer.Serialize(request.Language.Trim()) });

        return libraryIds;
    }

    private static DatabaseOptions? ValidateDatabase(SetupDatabase request, string prefix, ValidationErrors errors)
    {
        var field = prefix.Length == 0 ? "" : prefix + ".";

        switch (request.Provider)
        {
            case null:
                errors.Add(field + "provider", ErrorCodes.Required);
                return null;
            case DatabaseProvider.Postgres when string.IsNullOrWhiteSpace(request.ConnectionString):
                errors.Add(field + "connectionString", ErrorCodes.Required);
                return null;
            default:
                return new DatabaseOptions
                {
                    Provider = request.Provider,
                    ConnectionString = string.IsNullOrWhiteSpace(request.ConnectionString) ? null : request.ConnectionString.Trim(),
                };
        }
    }

    private async Task<IReadOnlyList<IReadOnlyList<string?>>> ValidateLibrariesAsync(
        IReadOnlyList<SetupLibrary> libraries, ValidationErrors errors, CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<IReadOnlyList<string?>>();

        for (var i = 0; i < libraries.Count; i++)
        {
            var library = libraries[i];
            var field = $"libraries[{i}]";

            LibraryRules.ValidateName(library.Name, field + ".name", errors);
            if (!string.IsNullOrWhiteSpace(library.Name) && !names.Add(TextNormalizer.Normalize(library.Name)))
                errors.Add(field + ".name", ErrorCodes.Duplicate);

            if (library.Folders is not { Count: > 0 })
            {
                errors.Add(field + ".folders", ErrorCodes.Required);
                result.Add([]);
                continue;
            }

            result.Add(await libraryRules.ValidateLocationsAsync(
                library.Folders.Select(f => (string?)f).ToList(), [], j => $"{field}.folders[{j}]", errors, cancellationToken));
        }

        return result;
    }

    private static DatabaseOptions? Required(ValidationErrors errors, string field)
    {
        errors.Add(field, ErrorCodes.Required);
        return null;
    }
}
