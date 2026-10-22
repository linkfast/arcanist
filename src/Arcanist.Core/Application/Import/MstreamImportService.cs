using System.Text.Json;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Application.Users;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Import.Mstream;
using Microsoft.AspNetCore.DataProtection;
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
/// <param name="Files">Its audio files on the mStream server (through its API; null for a folder), which can be
/// copied here when the folder is not on this server.</param>
public sealed record MstreamLibraryInfo(string Vpath, string Root, bool RootExists, Guid? SourceId, int? Files);

/// <param name="Exists">An Arcanist user has that name: the data goes to them, and their account is not changed.</param>
/// <param name="CanSignIn">Their mStream password will work here (otherwise an administrator sets a new one).</param>
public sealed record MstreamUserInfo(string UserName, bool IsAdmin, IReadOnlyList<string> Vpaths, bool Exists, bool CanSignIn);

/// <summary>What an mStream installation holds, before importing it.</summary>
/// <param name="Version">5 or 6 for an installation read from its folder; null for a server read through its API, which
/// does not tell. Through the API, the counts are the importing account's: the other users' playlists, ratings and
/// plays arrive when they first sign in here.</param>
public sealed record MstreamAnalysis(
    int? Version,
    IReadOnlyList<MstreamLibraryInfo> Libraries,
    IReadOnlyList<MstreamUserInfo> Users,
    int Playlists,
    int RatedTracks,
    int PlayedTracks);

/// <summary>What becomes of an mStream library: an existing library folder, a new library, or nothing (both null).</summary>
/// <param name="CopyFiles">From an mStream server: copy its files into that folder (at the same paths, never
/// overwriting), for a server on another machine.</param>
public sealed record MstreamLibraryChoice(string Vpath, Guid? SourceId, string? NewLibraryName, string? NewLibraryPath, bool CopyFiles = false);

/// <param name="Folder">The mStream installation on this server: its folder or configuration file.</param>
/// <param name="Server">Or an mStream server, through its API.</param>
/// <param name="ImportUsers">Create mStream's users (with their passwords and library access) and bring their data.</param>
public sealed record ImportMstreamInput(string? Folder, MstreamServerInput? Server, IReadOnlyList<MstreamLibraryChoice> Libraries, bool ImportUsers);

/// <summary>An import from mStream and what it brought.</summary>
/// <param name="Unmatched">Playlist songs, ratings and plays whose file is not in the catalog.</param>
/// <param name="FilesTotal">Files to copy from the server; then how many were copied, were there already or failed.</param>
/// <param name="CanRetry">The files that failed can be tried again (the mStream session is kept for that).</param>
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
    string? ErrorCode,
    int FilesTotal,
    int FilesCopied,
    int FilesPresent,
    int FilesFailed,
    bool CanRetry);

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
    /// <summary>Protects the mStream session kept to copy an import's files.</summary>
    internal const string SessionPurpose = "Arcanist.Mstream.Session";

    private MstreamReader Reader => services.GetRequiredService<MstreamReader>();

    public async Task<MstreamAnalysis> AnalyzeFolderAsync(string folder, CancellationToken cancellationToken)
    {
        guard.Admin();
        return await AnalysisAsync(await Reader.ReadAsync(folder ?? "", null, cancellationToken), cancellationToken);
    }

    public async Task<MstreamAnalysis> AnalyzeServerAsync(MstreamServerInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        return await AnalysisAsync(await Reader.ReadAsync(null, input, cancellationToken), cancellationToken);
    }

    public async Task<MstreamImportJob> ImportAsync(ImportMstreamInput input, CancellationToken cancellationToken)
    {
        var actor = guard.Admin();
        guard.RecentAuthentication();
        var choices = input.Libraries ?? [];
        if (input.Server is null && choices.Any(c => c.CopyFiles))
            throw ArcanistException.Field("libraries", ErrorCodes.InvalidFormat);
        var source = await Reader.ReadAsync(input.Folder, input.Server, cancellationToken);
        var vpaths = await MapLibrariesAsync(choices, source.Libraries, cancellationToken);
        var copy = choices.Where(c => c.CopyFiles && vpaths.Map.ContainsKey(c.Vpath)).Select(c => c.Vpath).ToHashSet(StringComparer.Ordinal);
        var import = await RecordAsync(db, source, vpaths.Map, vpaths.Created, input.ImportUsers, actor.UserId, Now(), cancellationToken,
            copy, Protector(services));
        await db.SaveChangesAsync(cancellationToken);
        accessCache.InvalidateAll();
        signal.Notify();
        return ToModel(import);
    }

    /// <summary>Tries again the files of an import from a server that could not be copied.</summary>
    public async Task<MstreamImportJob> RetryAsync(Guid id, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        var import = await db.MstreamImports.SingleOrDefaultAsync(i => i.Id == id, cancellationToken) ?? throw new ArcanistException.NotFound();
        if (!CanRetry(import))
            throw new ArcanistException.Conflict(ErrorCodes.ImportRunning);
        await db.MstreamImportFiles.Where(f => f.ImportId == id && f.Status == MstreamImportFileStatus.Failed)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Status, MstreamImportFileStatus.Pending).SetProperty(f => f.Reason, (string?)null), cancellationToken);
        import.FilesFailed = 0;
        import.ErrorCode = null;
        import.FinishedAt = null;
        import.Status = MstreamImportStatus.Copying;
        await db.SaveChangesAsync(cancellationToken);
        signal.Notify();
        return ToModel(import);
    }

    public async Task<IReadOnlyList<MstreamImportJob>> ListAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        var imports = await db.MstreamImports.AsNoTracking().OrderByDescending(i => i.CreatedAt).Take(20).ToListAsync(cancellationToken);
        return imports.Select(ToModel).ToList();
    }

    /// <summary>
    /// Removes a finished import from the history; what it brought stays. An import from an mStream server stays while
    /// some of its users have not signed in: their first sign-in asks that server through it.
    /// </summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        guard.Admin();
        var import = await db.MstreamImports.SingleOrDefaultAsync(i => i.Id == id, cancellationToken) ?? throw new ArcanistException.NotFound();
        if (import.Status is MstreamImportStatus.Waiting or MstreamImportStatus.Running)
            throw new ArcanistException.Conflict(ErrorCodes.ImportRunning);
        if (await InUseAsync(db, [import.Id], cancellationToken) is { Count: > 0 })
            throw new ArcanistException.Conflict(ErrorCodes.ImportInUse);
        db.MstreamImports.Remove(import);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Clears the history of migrations: every finished import from mStream (but those still needed to sign in) and
    /// from DLNA servers. What they brought stays. Returns how many went.
    /// </summary>
    public async Task<int> ClearHistoryAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        return await ClearAsync(db, cancellationToken) + await DlnaImportService.ClearAsync(db, cancellationToken)
                                                       + await ServerMigration.ArcanistImportService.ClearAsync(db, cancellationToken);
    }

    /// <summary>Removes every finished import that nobody needs any more from the history; returns how many.</summary>
    internal static async Task<int> ClearAsync(ArcanistDbContext db, CancellationToken cancellationToken)
    {
        var finished = await db.MstreamImports
            .Where(i => i.Status == MstreamImportStatus.Completed || i.Status == MstreamImportStatus.Failed)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);
        var inUse = await InUseAsync(db, finished, cancellationToken);
        var removable = finished.Where(id => !inUse.Contains(id)).ToList();
        return await db.MstreamImports.Where(i => removable.Contains(i.Id)).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>The imports some user still signs in through (an mStream server's, until their first sign-in).</summary>
    private static async Task<HashSet<Guid>> InUseAsync(ArcanistDbContext db, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var hashes = await db.Users.Where(u => u.PasswordHash.StartsWith(MstreamPasswords.ServerPrefix))
            .Select(u => u.PasswordHash)
            .ToListAsync(cancellationToken);
        return hashes.Select(MstreamPasswords.ServerImport).OfType<Guid>().Where(ids.Contains).ToHashSet();
    }

    /// <summary>
    /// Adds the import to <paramref name="db"/> (not saved), with the users it creates. The import worker finishes it:
    /// it copies the files of the vpaths in <paramref name="copy"/> from the server, then, once the scans of the
    /// libraries are over, brings the users' data: all of them from a folder; from a server, the importing account's
    /// (the others bring theirs when they first sign in).
    /// </summary>
    internal static async Task<MstreamImportEntity> RecordAsync(
        ArcanistDbContext db, MstreamSource source, IReadOnlyDictionary<string, (Guid SourceId, Guid LibraryId)> vpaths, int librariesCreated,
        bool importUsers, Guid? requestedBy, DateTime now, CancellationToken cancellationToken,
        IReadOnlyCollection<string>? copy = null, IDataProtector? sessions = null)
    {
        var import = new MstreamImportEntity
        {
            Kind = source.Kind,
            Source = source.Location,
            RequestedByUserId = requestedBy,
            CreatedAt = now,
            VpathMap = JsonSerializer.Serialize(vpaths.ToDictionary(v => v.Key, v => v.Value.SourceId)),
            UserMap = "{}",
            LibrariesCreated = librariesCreated,
        };

        var users = importUsers ? await ImportUsersAsync(db, import, source.Users, vpaths, now, cancellationToken) : [];
        import.UserMap = JsonSerializer.Serialize(users);

        // From a server: the importing account's data goes to the user of its name, or to whoever imports.
        if (source.Session is { } session && (session.Content.Playlists.Count > 0 || session.Content.Tracks.Count > 0)
            && (users.TryGetValue(session.UserName, out var owner) ? owner : requestedBy) is { } ownerId)
            import.PendingData = JsonSerializer.Serialize(new MstreamPendingData(ownerId, session.Content));
        if (source.Session is not null && copy is { Count: > 0 })
        {
            import.CopyMap = JsonSerializer.Serialize(copy.ToDictionary(v => v, v => vpaths[v].SourceId));
            import.Session = (sessions ?? throw new InvalidOperationException("A protector is needed to keep the session.")).Protect(source.Session.Token);
        }

        import.Status = import.CopyMap is not null ? MstreamImportStatus.Copying
            : import.PendingData is not null || import.Kind == MstreamImportKind.Folder && users.Count > 0 ? MstreamImportStatus.Waiting
            : MstreamImportStatus.Completed;
        import.FinishedAt = import.Status == MstreamImportStatus.Completed ? now : null;
        db.MstreamImports.Add(import);
        return import;
    }

    private async Task<MstreamAnalysis> AnalysisAsync(MstreamSource source, CancellationToken cancellationToken)
    {
        var sources = await db.LibrarySources.AsNoTracking().Select(s => new { s.Id, s.Location }).ToListAsync(cancellationToken);
        var names = await db.Users.Select(u => u.NormalizedUserName).ToListAsync(cancellationToken);
        return MstreamReader.Analyze(source, sources.Select(s => (s.Id, s.Location)).ToList(), names);
    }

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
    private static async Task<Dictionary<string, Guid>> ImportUsersAsync(
        ArcanistDbContext db, MstreamImportEntity import, IReadOnlyList<MstreamUserData> sourceUsers,
        IReadOnlyDictionary<string, (Guid SourceId, Guid LibraryId)> vpaths, DateTime now, CancellationToken cancellationToken)
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
                CreatedAt = now,
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
        import.Playlists, import.Ratings, import.Plays, import.Unmatched, import.ErrorCode,
        import.FilesTotal, import.FilesCopied, import.FilesPresent, import.FilesFailed, CanRetry(import));

    private static bool CanRetry(MstreamImportEntity import) =>
        import is { Kind: MstreamImportKind.Server, Session: not null, CopyMap: not null, Status: MstreamImportStatus.Completed or MstreamImportStatus.Failed }
        && (import.FilesFailed > 0 || import.ErrorCode is not null);

    internal static IDataProtector Protector(IServiceProvider services) =>
        services.GetRequiredService<IDataProtectionProvider>().CreateProtector(SessionPurpose);

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
