using System.Text.Json;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Application.Users;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Import.Mstream;
using Microsoft.EntityFrameworkCore;
using MstreamImportEntity = Arcanist.Core.Data.Entities.MstreamImport;

namespace Arcanist.Core.Application.Import;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>An mStream server and an administrator account of it.</summary>
public sealed record MstreamServerInput(string Url, string UserName, string Password);

/// <param name="Vpath">mStream's name for the library.</param>
/// <param name="Root">Its folder, on the mStream server.</param>
/// <param name="RootExists">The folder exists on this server.</param>
/// <param name="SourceId">The library folder of Arcanist at the same place, if there is one.</param>
public sealed record MstreamLibraryInfo(string Vpath, string Root, bool RootExists, Guid? SourceId);

/// <param name="Exists">An Arcanist user has that name: the data goes to them, and their account is not changed.</param>
/// <param name="CanSignIn">Their mStream password will work here (otherwise an administrator sets a new one).</param>
public sealed record MstreamUserInfo(string UserName, bool IsAdmin, IReadOnlyList<string> Vpaths, bool Exists, bool CanSignIn);

/// <summary>What an mStream installation holds, before importing it.</summary>
/// <param name="Version">5 or 6 for an installation read from its folder; null for a server read through its API, which
/// does not tell. Through the API, each user's playlists, ratings and plays arrive when they first sign in here.</param>
public sealed record MstreamAnalysis(
    int? Version,
    IReadOnlyList<MstreamLibraryInfo> Libraries,
    IReadOnlyList<MstreamUserInfo> Users,
    int Playlists,
    int RatedTracks,
    int PlayedTracks);

/// <summary>What becomes of an mStream library: an existing library folder, a new library, or nothing (both null).</summary>
public sealed record MstreamLibraryChoice(string Vpath, Guid? SourceId, string? NewLibraryName, string? NewLibraryPath);

/// <param name="Folder">The mStream installation on this server: its folder or configuration file.</param>
/// <param name="Server">Or an mStream server, through its API.</param>
/// <param name="ImportUsers">Create mStream's users (with their passwords and library access) and bring their data.</param>
public sealed record ImportMstreamInput(string? Folder, MstreamServerInput? Server, IReadOnlyList<MstreamLibraryChoice> Libraries, bool ImportUsers);

/// <summary>An import from mStream and what it brought.</summary>
/// <param name="Unmatched">Playlist songs, ratings and plays whose file is not in the catalog.</param>
public sealed record MstreamImportJob(
    Guid Id,
    MstreamImportKind Kind,
    string Source,
    MstreamImportStatus Status,
    DateTime CreatedAt,
    DateTime? FinishedAt,
    int LibrariesCreated,
    int UsersCreated,
    int Playlists,
    int Ratings,
    int Plays,
    int Unmatched,
    string? ErrorCode);

/// <summary>
/// Imports an mStream installation: libraries, users (who keep their passwords) and their playlists, ratings and
/// play counts. Administrators only; importing needs a recent authentication.
/// </summary>
public sealed class MstreamImportService(
    ArcanistDbContext db,
    AccessGuard guard,
    LibraryService libraries,
    UserAccessCache accessCache,
    TimeProvider time,
    MstreamImportSignal signal,
    IServiceProvider services)
{
    private MstreamServerClient Client => services.GetRequiredService<MstreamServerClient>();

    public async Task<MstreamAnalysis> AnalyzeFolderAsync(string folder, CancellationToken cancellationToken)
    {
        guard.Admin();
        var data = MstreamFolder.Read(folder ?? "");
        return await AnalysisAsync(data.Version, data.Libraries, data.Users, data.Content.Values.ToList(), cancellationToken);
    }

    public async Task<MstreamAnalysis> AnalyzeServerAsync(MstreamServerInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        var (_, read) = await ReadServerAsync(input, cancellationToken);
        return await AnalysisAsync(null, read.Libraries, read.Users, [], cancellationToken);
    }

    public async Task<MstreamImportJob> ImportAsync(ImportMstreamInput input, CancellationToken cancellationToken)
    {
        var actor = guard.Admin();
        guard.RecentAuthentication();
        if ((input.Folder is null) == (input.Server is null))
            throw ArcanistException.Field("folder", ErrorCodes.Required);

        IReadOnlyList<MstreamLibraryData> sourceLibraries;
        IReadOnlyList<MstreamUserData> sourceUsers;
        string source;
        if (input.Folder is { } folder)
        {
            var data = MstreamFolder.Read(folder);
            (sourceLibraries, sourceUsers, source) = (data.Libraries, data.Users, folder.Trim());
        }
        else
        {
            var (url, read) = await ReadServerAsync(input.Server!, cancellationToken);
            (sourceLibraries, sourceUsers, source) = (read.Libraries, read.Users, url.AbsoluteUri.TrimEnd('/'));
        }

        var vpaths = await MapLibrariesAsync(input.Libraries ?? [], sourceLibraries, cancellationToken);
        var import = new MstreamImportEntity
        {
            Kind = input.Folder is null ? MstreamImportKind.Server : MstreamImportKind.Folder,
            Source = source,
            RequestedByUserId = actor.UserId,
            CreatedAt = Now(),
            VpathMap = "{}",
            UserMap = "{}",
            LibrariesCreated = vpaths.Created,
        };

        var users = input.ImportUsers ? await ImportUsersAsync(import, sourceUsers, vpaths.Map, cancellationToken) : [];
        import.VpathMap = JsonSerializer.Serialize(vpaths.Map.ToDictionary(v => v.Key, v => v.Value.SourceId));
        import.UserMap = JsonSerializer.Serialize(users);
        // Through the API there is nothing more to do now: each user brings their data when they sign in.
        import.Status = import.Kind == MstreamImportKind.Folder && users.Count > 0 ? MstreamImportStatus.Waiting : MstreamImportStatus.Completed;
        import.FinishedAt = import.Status == MstreamImportStatus.Completed ? Now() : null;
        db.MstreamImports.Add(import);
        await db.SaveChangesAsync(cancellationToken);
        accessCache.InvalidateAll();
        signal.Notify();
        return ToModel(import);
    }

    public async Task<IReadOnlyList<MstreamImportJob>> ListAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        var imports = await db.MstreamImports.AsNoTracking().OrderByDescending(i => i.CreatedAt).Take(20).ToListAsync(cancellationToken);
        return imports.Select(ToModel).ToList();
    }

    private async Task<(Uri Url, (IReadOnlyList<MstreamLibraryData> Libraries, IReadOnlyList<MstreamUserData> Users) Read)> ReadServerAsync(
        MstreamServerInput input, CancellationToken cancellationToken)
    {
        var url = MstreamServerClient.ParseUrl(input.Url) ?? throw ArcanistException.Field("url", ErrorCodes.InvalidFormat);
        var token = await Client.LoginAsync(url, input.UserName ?? "", input.Password ?? "", cancellationToken)
                    ?? throw ArcanistException.Field("password", ErrorCodes.InvalidCredentials);
        return (url, await Client.ReadAsync(url, token, cancellationToken));
    }

    private async Task<MstreamAnalysis> AnalysisAsync(
        int? version, IReadOnlyList<MstreamLibraryData> sourceLibraries, IReadOnlyList<MstreamUserData> sourceUsers,
        IReadOnlyList<MstreamUserContent> content, CancellationToken cancellationToken)
    {
        var sources = await db.LibrarySources.AsNoTracking().Select(s => new { s.Id, s.Location }).ToListAsync(cancellationToken);
        var names = await db.Users.Select(u => u.NormalizedUserName).ToListAsync(cancellationToken);
        var server = version is null;

        return new MstreamAnalysis(
            version,
            sourceLibraries.Select(l => new MstreamLibraryInfo(l.Name, l.Root, l.Root.Length > 0 && Directory.Exists(l.Root),
                sources.FirstOrDefault(s => SamePath(s.Location, l.Root))?.Id)).ToList(),
            sourceUsers.Select(u => new MstreamUserInfo(u.Name, u.IsAdmin, u.Vpaths, names.Contains(TextNormalizer.Normalize(u.Name)),
                server || u is { PasswordHash.Length: > 0, Salt.Length: > 0 })).ToList(),
            content.Sum(c => c.Playlists.Count),
            content.Sum(c => c.Tracks.Count(t => t.Rating > 0)),
            content.Sum(c => c.Tracks.Count(t => t.PlayCount > 0)));
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>The library folder of each chosen vpath, creating the new libraries.</summary>
    private async Task<(Dictionary<string, (Guid SourceId, Guid LibraryId)> Map, int Created)> MapLibrariesAsync(
        IReadOnlyList<MstreamLibraryChoice> choices, IReadOnlyList<MstreamLibraryData> sourceLibraries, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var existing = new Dictionary<string, (Guid, Guid)>(StringComparer.Ordinal);
        for (var i = 0; i < choices.Count; i++)
        {
            var choice = choices[i];
            if (sourceLibraries.All(l => l.Name != choice.Vpath))
                errors.Add($"libraries[{i}].vpath", ErrorCodes.NotFound);
            else if (choice.SourceId is { } sourceId)
            {
                var source = await db.LibrarySources.AsNoTracking().Where(s => s.Id == sourceId).Select(s => new { s.Id, s.LibraryId }).SingleOrDefaultAsync(cancellationToken);
                if (source is null)
                    errors.Add($"libraries[{i}].sourceId", ErrorCodes.NotFound);
                else
                    existing[choice.Vpath] = (source.Id, source.LibraryId);
            }
        }

        errors.ThrowIfAny();

        var map = new Dictionary<string, (Guid SourceId, Guid LibraryId)>(existing, StringComparer.Ordinal);
        var created = 0;
        for (var i = 0; i < choices.Count; i++)
        {
            var choice = choices[i];
            if (choice.SourceId is not null || string.IsNullOrWhiteSpace(choice.NewLibraryPath) || map.ContainsKey(choice.Vpath))
                continue;
            Libraries.Library library;
            try
            {
                library = await libraries.CreateAsync(new CreateLibraryInput(
                    string.IsNullOrWhiteSpace(choice.NewLibraryName) ? choice.Vpath : choice.NewLibraryName, false, 24,
                    [new LibrarySourceInput(choice.NewLibraryPath)]), cancellationToken);
            }
            catch (ArcanistException.Invalid invalid)
            {
                // Reported on the choice: a name already taken, a folder that does not exist…
                var field = invalid.Errors.ToDictionary().Keys.FirstOrDefault() ?? "";
                throw ArcanistException.Field($"libraries[{i}].{(field.StartsWith("sources", StringComparison.Ordinal) ? "newLibraryPath" : "newLibraryName")}",
                    invalid.Errors.ToDictionary().Values.First()[0]);
            }

            map[choice.Vpath] = (library.Sources![0].Id, library.Id);
            created++;
        }

        return (map, created);
    }

    /// <summary>
    /// Creates the users Arcanist does not have yet: administrators stay administrators, the rest may download and
    /// get the libraries of their vpaths. Users with names Arcanist does not accept are left out.
    /// </summary>
    private async Task<Dictionary<string, Guid>> ImportUsersAsync(
        MstreamImportEntity import, IReadOnlyList<MstreamUserData> sourceUsers, IReadOnlyDictionary<string, (Guid SourceId, Guid LibraryId)> vpaths,
        CancellationToken cancellationToken)
    {
        var users = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var source in sourceUsers)
        {
            var normalized = TextNormalizer.Normalize(source.Name);
            if (await db.Users.Where(u => u.NormalizedUserName == normalized).Select(u => (Guid?)u.Id).SingleOrDefaultAsync(cancellationToken) is { } existing)
            {
                users[source.Name] = existing;
                continue;
            }

            var errors = new ValidationErrors();
            UserRules.ValidateUserName(source.Name, "userName", errors);
            if (!errors.IsEmpty)
                continue;

            var user = new User
            {
                UserName = source.Name.Trim(),
                NormalizedUserName = normalized,
                Role = source.IsAdmin ? UserRole.Admin : UserRole.User,
                Permissions = source.IsAdmin ? Permissions.None : Permissions.Download,
                PasswordHash = import.Kind == MstreamImportKind.Server
                    ? MstreamPasswords.Server(import.Id)
                    : source is { PasswordHash.Length: > 0, Salt.Length: > 0 } ? MstreamPasswords.Hash(source.Salt, source.PasswordHash) : "",
                CreatedAt = Now(),
            };
            if (!source.IsAdmin)
            {
                user.LibraryAccess.AddRange(source.Vpaths.Where(vpaths.ContainsKey).Select(v => vpaths[v].LibraryId).Distinct()
                    .Select(id => new LibraryAccess { UserId = user.Id, LibraryId = id }));
            }

            db.Users.Add(user);
            users[source.Name] = user.Id;
            import.UsersCreated++;
        }

        return users;
    }

    internal static MstreamImportJob ToModel(MstreamImportEntity import) => new(
        import.Id, import.Kind, import.Source, import.Status, import.CreatedAt, import.FinishedAt, import.LibrariesCreated, import.UsersCreated,
        import.Playlists, import.Ratings, import.Plays, import.Unmatched, import.ErrorCode);

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
