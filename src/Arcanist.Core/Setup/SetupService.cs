using System.Text.Json;
using Arcanist.Core.Api;
using Arcanist.Core.Application;
using LibraryRules = Arcanist.Core.Application.Libraries.LibraryRules;
using Arcanist.Core.Application.Import;
using Arcanist.Core.Application.Users;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Arcanist.Core.Import.Mstream;
using Arcanist.Core.Media;
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

/// <param name="Migrating">Set up, but the migration the wizard started (mStream: users and their data) is still
/// running: the panel shows its progress until it ends.</param>
public sealed record SetupStatus(bool Completed, bool RequiresCode, SetupDatabaseStatus Database, string DataDirectory, bool Migrating = false);

/// <param name="LockedByEnvironment">The database is set outside the wizard (environment variables, appsettings),
/// so the wizard skips that step and ignores <see cref="SetupRequest.Database"/>.</param>
public sealed record SetupDatabaseStatus(bool Configured, bool LockedByEnvironment, DatabaseProvider? Provider, string DefaultSqlitePath);

public sealed record SetupDatabase(DatabaseProvider? Provider, string? ConnectionString);

public sealed record SetupAdmin(string? UserName, string? Password, string? DisplayName);

public sealed record SetupLibrary(string? Name, IReadOnlyList<string>? Folders, bool VisibleToAll = true);

/// <summary>An mStream installation to import while setting up: its folder on this server, or its server.</summary>
public sealed record SetupMstreamSource(string? Folder, MstreamServerInput? Server);

/// <param name="Vpath">mStream's name for the library.</param>
/// <param name="Folder">Where its music is on this server: one of the folders of <see cref="SetupRequest.Libraries"/>.</param>
/// <param name="CopyFiles">From an mStream server on another machine: copy its files into that folder.</param>
public sealed record SetupMstreamLibrary(string? Vpath, string? Folder, bool CopyFiles = false);

/// <summary>Imports an mStream installation into the new server.</summary>
/// <param name="Libraries">The mStream libraries whose data comes along, and where they are now. Others are left out.</param>
/// <param name="ImportUsers">Create mStream's users (with their passwords and library access) and bring their data. A
/// user with the administrator's name is the administrator: their data goes to them.</param>
public sealed record SetupMstreamImport(string? Folder, MstreamServerInput? Server, IReadOnlyList<SetupMstreamLibrary>? Libraries, bool ImportUsers);

/// <summary>Everything the wizard collects, submitted at once so setup never ends half-done.</summary>
/// <param name="FfmpegPath">ffmpeg to use; null to find it automatically (or download it later from the panel).</param>
/// <param name="OnlineServices">Online services to turn on; none when null.</param>
/// <param name="RatingMode">How users rate tracks; five stars when null. Can be changed later in the settings.</param>
/// <param name="Privacy">No telemetry, and which outside services may be used; they limit the statistics and online
/// services asked for (and with no outside services at all, the Ultimate Cloud extension starts off).</param>
/// <param name="Mstream">An mStream installation to import, or null. Its progress shows in <c>mstreamImports</c>
/// once signed in: the users' data comes once the first scans are over.</param>
public sealed record SetupRequest(
    SetupDatabase? Database,
    SetupAdmin? Admin,
    IReadOnlyList<SetupLibrary>? Libraries,
    bool? ShareAnonymousStatistics,
    string? Language,
    string? FfmpegPath = null,
    Application.Online.OnlineServicesSettings? OnlineServices = null,
    RatingMode? RatingMode = null,
    SetupMstreamImport? Mstream = null,
    Application.Server.SetPrivacyInput? Privacy = null);

/// <summary>What the wizard shows about ffmpeg: the one at the given path, or the one found automatically.</summary>
public sealed record SetupFfmpegCheck(bool Available, string? Path, string? Version, FfmpegSource? Source);

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
    FfmpegLocator ffmpeg,
    Application.Listening.RatingSettings ratings,
    IServiceProvider services,
    MstreamImportSignal mstreamSignal,
    TimeProvider time,
    ILogger<SetupService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private MstreamReader Mstream => services.GetRequiredService<MstreamReader>();

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

    /// <summary>Whether the migration the wizard started is still running.</summary>
    public async Task<bool> MigratingAsync(CancellationToken cancellationToken)
    {
        if (!IsCompleted || databaseOptions.CurrentValue.Provider is null)
            return false;
        try
        {
            await using var db = ArcanistDbContextFactory.Create(databaseOptions.CurrentValue, paths);
            var value = await db.Settings.AsNoTracking().Where(s => s.Key == SettingKeys.SetupMigration).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken);
            if (value is null || JsonSerializer.Deserialize<Guid?>(value) is not { } id)
                return false;
            return await db.MstreamImports.AsNoTracking()
                .AnyAsync(i => i.Id == id && (i.Status == MstreamImportStatus.Waiting || i.Status == MstreamImportStatus.Running), cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogDebug(e, "Could not read the setup migration");
            return false;
        }
    }

    /// <summary>Checks the ffmpeg at <paramref name="path"/>, or which one would be found automatically.</summary>
    public async Task<SetupFfmpegCheck> CheckFfmpegAsync(string? path, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            var version = await FfmpegLocator.ProbeAsync(path.Trim(), cancellationToken);
            return new SetupFfmpegCheck(version is not null, path.Trim(), version, version is null ? null : FfmpegSource.Configured);
        }

        var found = (await ffmpeg.RefreshAsync(cancellationToken)).Installation;
        return new SetupFfmpegCheck(found is not null, found?.Path, found?.Version, found?.Source);
    }

    /// <summary>What an mStream installation holds, to choose what to import. There are no libraries or users to match yet.</summary>
    public async Task<MstreamAnalysis> AnalyzeMstreamAsync(SetupMstreamSource request, CancellationToken cancellationToken) =>
        MstreamReader.Analyze(await Mstream.ReadAsync(request.Folder, request.Server, cancellationToken), [], []);

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

            if (!string.IsNullOrWhiteSpace(request.FfmpegPath) && await FfmpegLocator.ProbeAsync(request.FfmpegPath.Trim(), cancellationToken) is null)
                errors.Add("ffmpegPath", ErrorCodes.NotFfmpeg);

            var mstreamImport = request.Mstream is null ? null : await ValidateMstreamAsync(request.Mstream, libraries, errors, cancellationToken);

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

            IReadOnlyList<Library> created;
            Data.Entities.MstreamImport? imported = null;
            await using (var db = ArcanistDbContextFactory.Create(database, paths))
            {
                if (await db.Users.AnyAsync(cancellationToken))
                {
                    // A database that already has accounts (for example a reused PostgreSQL) is not taken over.
                    errors.Add("database", ErrorCodes.DatabaseNotEmpty);
                    return new SetupResult.Invalid(errors);
                }

                // All or nothing: a setup that fails here can be submitted again.
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                (var adminId, created) = Seed(db, request, libraries, folders);
                await db.SaveChangesAsync(cancellationToken);
                // The import worker waits until these libraries have been scanned, so their files can be matched.
                if (mstreamImport is { } found)
                {
                    imported = await RecordMstreamImportAsync(db, request.Mstream!, found.Source, found.Vpaths, created, adminId, cancellationToken);
                    db.Settings.Add(new Setting { Key = SettingKeys.SetupMigration, Value = JsonSerializer.Serialize<Guid?>(imported.Id) });
                    await db.SaveChangesAsync(cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
            }

            // Committed: the rest happens even if the wizard goes away, or the first scans would never be queued.
            if (request.Privacy?.ExternalServices == Application.Server.ExternalServices.None)
                await Application.Server.PrivacyService.TurnOffUltimateCloudAsync(
                    settingsWriter, services.GetRequiredService<Extensions.ExtensionHost>(), CancellationToken.None);
            await settingsWriter.SetAsync(new Dictionary<string, object?> { [SetupOptions.CompletedKey] = true }, CancellationToken.None);
            setupCode.Discard();

            // The chosen ffmpeg and rating mode are in the database now.
            await ffmpeg.RefreshAsync(CancellationToken.None);
            ratings.Reset();
            await ratings.GetAsync(CancellationToken.None);

            // The wizard's last step shows this first scan in progress.
            foreach (var library in created)
                await scans.EnqueueAsync(library.Id, ScanKind.Full, requestedBy: null, CancellationToken.None);

            if (imported is not null)
            {
                mstreamSignal.Notify();
                logger.LogInformation("Importing mStream from {Source}: {Libraries} library(ies), {Users} user(s) created",
                    imported.Source, imported.LibrariesCreated, imported.UsersCreated);
            }

            logger.LogInformation("Setup completed: {Provider} database, administrator {UserName}, {LibraryCount} library(ies)",
                database.Provider, request.Admin!.UserName, libraries.Count);

            return new SetupResult.Completed();
        }
        finally
        {
            _gate.Release();
        }
    }

    private (Guid AdminId, IReadOnlyList<Library> Libraries) Seed(ArcanistDbContext db, SetupRequest request, IReadOnlyList<SetupLibrary> libraries, IReadOnlyList<IReadOnlyList<string?>> folders)
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

        var created = new List<Library>();
        for (var i = 0; i < libraries.Count; i++)
        {
            var library = new Library { Number = i + 1, Name = libraries[i].Name!.Trim(), VisibleToAll = libraries[i].VisibleToAll };
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
            created.Add(library);
        }

        var privacy = request.Privacy is { } chosen
            ? new Application.Server.Privacy(chosen.NoTelemetry, chosen.ExternalServices)
            : Application.Server.PrivacySettings.Default;
        db.Settings.Add(new Setting { Key = SettingKeys.Privacy, Value = JsonSerializer.Serialize(privacy, JsonSerializerOptions.Web) });
        db.Settings.Add(new Setting
        {
            Key = SettingKeys.ShareAnonymousStatistics,
            Value = JsonSerializer.Serialize(request.ShareAnonymousStatistics!.Value
                                             && privacy is { NoTelemetry: false, ExternalServices: not Application.Server.ExternalServices.None }),
        });

        if (!string.IsNullOrWhiteSpace(request.Language))
            db.Settings.Add(new Setting { Key = SettingKeys.DefaultLanguage, Value = JsonSerializer.Serialize(request.Language.Trim()) });

        if (!string.IsNullOrWhiteSpace(request.FfmpegPath))
            db.Settings.Add(new Setting { Key = SettingKeys.FfmpegPath, Value = JsonSerializer.Serialize(request.FfmpegPath.Trim()) });

        if (request.OnlineServices is { } online && privacy.ExternalServices == Application.Server.ExternalServices.All)
            db.Settings.Add(new Setting { Key = SettingKeys.OnlineServices, Value = JsonSerializer.Serialize(online, JsonSerializerOptions.Web) });

        db.Settings.Add(new Setting { Key = SettingKeys.RatingMode, Value = JsonSerializer.Serialize(request.RatingMode ?? Application.Listening.RatingSettings.Default, JsonSerializerOptions.Web) });

        return (admin.Id, created);
    }

    /// <summary>
    /// Reads the mStream installation and checks the chosen libraries: each must be one of mStream's, and its folder
    /// one of the new libraries'. Returns the installation and, for each vpath, which library and folder it goes to.
    /// </summary>
    private async Task<(MstreamSource Source, IReadOnlyDictionary<string, (int Library, int Folder)> Vpaths)?> ValidateMstreamAsync(
        SetupMstreamImport request, IReadOnlyList<SetupLibrary> libraries, ValidationErrors errors, CancellationToken cancellationToken)
    {
        MstreamSource source;
        try
        {
            source = await Mstream.ReadAsync(request.Folder, request.Server, cancellationToken);
        }
        catch (ArcanistException.Invalid invalid)
        {
            foreach (var (field, codes) in invalid.Errors.ToDictionary())
                errors.Add(field is "url" or "userName" or "password" ? $"mstream.server.{field}" : $"mstream.{field}", codes[0]);
            return null;
        }
        catch (ArcanistException e)
        {
            errors.Add("mstream", e.Code);
            return null;
        }

        var folders = libraries
            .SelectMany((library, l) => (library.Folders ?? []).Select((folder, f) => (Path: folder, Position: (l, f))))
            .ToList();
        var vpaths = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        var chosen = request.Libraries ?? [];
        for (var i = 0; i < chosen.Count; i++)
        {
            var (vpath, folder) = (chosen[i].Vpath ?? "", chosen[i].Folder ?? "");
            var target = folders.FindIndex(f => MstreamReader.SamePath(f.Path, folder));
            if (source.Libraries.All(l => l.Name != vpath))
                errors.Add($"mstream.libraries[{i}].vpath", ErrorCodes.NotFound);
            else if (target < 0)
                errors.Add($"mstream.libraries[{i}].folder", ErrorCodes.NotFound);
            else
                vpaths.TryAdd(vpath, folders[target].Position);
        }

        return (source, vpaths);
    }

    private async Task<Data.Entities.MstreamImport> RecordMstreamImportAsync(
        ArcanistDbContext db, SetupMstreamImport request, MstreamSource source, IReadOnlyDictionary<string, (int Library, int Folder)> vpaths,
        IReadOnlyList<Library> libraries, Guid adminId, CancellationToken cancellationToken)
    {
        // The libraries' folders are in the order they were submitted.
        var map = vpaths.ToDictionary(v => v.Key, v =>
        {
            var library = libraries[v.Value.Library];
            return (library.Sources[v.Value.Folder].Id, library.Id);
        }, StringComparer.Ordinal);

        var copy = request.Server is null
            ? []
            : (request.Libraries ?? []).Where(l => l.CopyFiles && l.Vpath is not null && map.ContainsKey(l.Vpath)).Select(l => l.Vpath!).ToHashSet(StringComparer.Ordinal);
        var import = await MstreamImportService.RecordAsync(db, source, map, map.Values.Select(v => v.Item2).Distinct().Count(),
            request.ImportUsers, adminId, time.GetUtcNow().UtcDateTime, cancellationToken, copy, MstreamImportService.Protector(services));
        await db.SaveChangesAsync(cancellationToken);
        return import;
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

            if (library.Folders.Count > 1)
                errors.Add(field + ".folders", ErrorCodes.TooMany);

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
