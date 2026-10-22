using System.Text.Json;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Application.Users;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Dlna;
using Arcanist.Core.ServerMigration;
using Arcanist.Core.Application.P2P;
using Arcanist.Core.P2P;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.ServerMigration;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

public enum ArcanistServerVia
{
    /// <summary>Found on the local networks (its DLNA server answered).</summary>
    Network,

    /// <summary>Paired with this server through the P2P network (it shares libraries, or receives them).</summary>
    Paired,
}

/// <summary>Another Arcanist this server can reach.</summary>
/// <param name="Url">Its address, when found on the network.</param>
/// <param name="NodeId">Its P2P node, when paired.</param>
public sealed record ArcanistServer(string Name, string? Url, string? NodeId, ArcanistServerVia Via);

/// <summary>Where the other Arcanist is, and its migration code (made in its panel, Import page).</summary>
/// <param name="Url">Its address (<c>host</c>, <c>host:port</c> or a URL; port 8484 when none is said)…</param>
/// <param name="NodeId">…or the P2P node of a paired server…</param>
/// <param name="Link">…or its P2P migration link (<c>arcanist://p2p?kind=migrate…</c>), which carries the code. It
/// turns the P2P network on.</param>
/// <param name="Code">The migration code, with <see cref="Url"/> or <see cref="NodeId"/>.</param>
public sealed record ArcanistSourceInput(string? Url, string? NodeId, string? Link, string? Code);

/// <param name="Bytes">Size of its audio files.</param>
/// <param name="SameName">A library of this server with the same name, the obvious place for its music.</param>
public sealed record ArcanistSourceLibrary(Guid Id, string Name, bool VisibleToAll, int Files, long Bytes, int Tracks, Guid? SameName);

/// <param name="Exists">This server has a user with that name: their data goes to them and nothing of theirs changes.</param>
/// <param name="SecondFactor">They sign in with a second factor there: they pair it again at their first sign-in here.</param>
public sealed record ArcanistSourceUser(
    Guid Id,
    string UserName,
    string? DisplayName,
    UserRole Role,
    bool IsDisabled,
    bool HasPassword,
    bool SecondFactor,
    bool Exists);

/// <param name="Installed">This server has it already (in any version).</param>
/// <param name="Available">It can be brought: built in here, or its package can be downloaded (from the
/// marketplace, or from the other server).</param>
public sealed record ArcanistSourceComponent(string Id, string Name, string Version, bool Enabled, bool BuiltIn, bool OpenSource, bool Installed, bool Available);

/// <summary>What another Arcanist holds, to choose what to bring.</summary>
public sealed record ArcanistSourceAnalysis(
    string ServerName,
    string Version,
    bool ViaP2P,
    IReadOnlyList<ArcanistSourceLibrary> Libraries,
    IReadOnlyList<ArcanistSourceUser> Users,
    IReadOnlyList<ArcanistSourceComponent> Extensions,
    IReadOnlyList<ArcanistSourceComponent> Players,
    string? DefaultPlayer);

/// <summary>Where a library of the other server goes: an existing library of this one, or a new one.</summary>
/// <param name="LibraryId">A library with a folder on this server. Several libraries may go to the same one.</param>
/// <param name="NewLibraryName">Or a new library, with this name…</param>
/// <param name="NewLibraryPath">…in this folder of this server. Choices with the same folder make one library.</param>
public sealed record ArcanistLibraryChoice(Guid SourceLibraryId, Guid? LibraryId, string? NewLibraryName, string? NewLibraryPath);

/// <summary>What to bring from another Arcanist.</summary>
/// <param name="Users">The other server's users to bring (by id). Those whose name exists here are not changed:
/// only their data comes.</param>
/// <param name="CopyPasswords">New users keep their passwords (never app passwords, sessions, paired devices or second
/// factors); without it they have none until an administrator sets one.</param>
/// <param name="Permissions">New users keep their role, permissions and library access; without it they are users
/// who may download and see the libraries visible to everyone.</param>
/// <param name="UserData">Ratings, play counts and history, playlists and wish lists of the chosen users. Plays brought
/// this way never count twice.</param>
/// <param name="Settings">The server's settings (ratings, privacy, online services, language, DLNA, mStream,
/// transcoding, AI…), not those of this machine (database, folders, P2P, Ultimate Cloud).</param>
/// <param name="Extensions">Its extensions and web players: built-in ones switched on or off as there, the others
/// installed (from the marketplace when it has them) and its default player.</param>
public sealed record StartArcanistImportInput(
    ArcanistSourceInput Source,
    IReadOnlyList<ArcanistLibraryChoice> Libraries,
    IReadOnlyList<Guid> Users,
    bool CopyPasswords,
    bool Permissions,
    bool UserData,
    bool Settings,
    bool Extensions);

/// <summary>A file that could not be copied.</summary>
/// <param name="Reason"><c>download_failed</c> or <c>write_failed</c>.</param>
public sealed record ArcanistImportFailure(string Path, string Reason);

/// <summary>A migration from another Arcanist, and how far it got.</summary>
/// <param name="BytesTotal">Size of the files to copy.</param>
/// <param name="FilesPresent">Files this server had already.</param>
/// <param name="Unmatched">References to songs this server does not have (ratings, plays, playlist entries).</param>
/// <param name="CanRetry">Some files failed, or it stopped: it can go on.</param>
/// <param name="Failures">The first 50 files that failed.</param>
public sealed record ArcanistImportJob(
    Guid Id,
    string SourceName,
    string? SourceVersion,
    ArcanistImportStatus Status,
    DateTime CreatedAt,
    DateTime? FinishedAt,
    string? ErrorCode,
    IReadOnlyList<string> Libraries,
    int UsersCreated,
    int UsersMapped,
    int LibrariesCreated,
    bool SettingsApplied,
    int ExtensionsInstalled,
    int PlayersInstalled,
    int FilesTotal,
    int FilesCopied,
    int FilesPresent,
    int FilesFailed,
    long BytesTotal,
    long CopiedBytes,
    int Ratings,
    int PlayCounts,
    int Plays,
    int Playlists,
    int Wishes,
    int Unmatched,
    bool CanRetry,
    IReadOnlyList<ArcanistImportFailure> Failures);

/// <summary>The choices of a migration, kept with it for the worker.</summary>
internal sealed record ArcanistImportPlan(
    List<Guid> Users,
    bool CopyPasswords,
    bool Permissions,
    bool UserData,
    bool Settings,
    bool Extensions);

/// <summary>
/// Migrations from another Arcanist (a server moving to new hardware, or merging into this one) through its export
/// API, with the migration code its administrator made: its users, libraries and music, the users' data, its
/// settings, extensions and players. Reading the other server never counts as plays there. Administrators only;
/// starting one needs a recent authentication.
/// </summary>
public sealed class ArcanistImportService(
    ArcanistDbContext db,
    AccessGuard guard,
    LibraryService libraries,
    UserAccessCache accessCache,
    TimeProvider time,
    IServiceProvider services,
    ILogger<ArcanistImportService> logger)
{
    internal const string CodePurpose = "Arcanist.Migration.Code";

    private ArcanistSourceClient Client => services.GetRequiredService<ArcanistSourceClient>();
    private ArcanistImportWorker Worker => services.GetRequiredService<ArcanistImportWorker>();

    /// <summary>Other Arcanist servers: those on the local networks (a few seconds' search) and those paired through P2P.</summary>
    public async Task<IReadOnlyList<ArcanistServer>> ServersAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        var self = (await services.GetRequiredService<DlnaSettings>().DeviceIdAsync(cancellationToken)).ToString();
        var found = (await DiscoverAsync(services.GetRequiredService<MediaServerClient>(), self, cancellationToken)).ToList();

        var remotes = await db.P2PRemotes.AsNoTracking().Where(r => r.Status != P2PRemoteStatus.Revoked)
            .Select(r => new { r.NodeId, r.Name }).ToListAsync(cancellationToken);
        var peers = await db.P2PPeers.AsNoTracking().Where(p => p.Kind == P2PPeerKind.Server && p.RevokedAt == null)
            .Select(p => new { p.NodeId, p.Name }).ToListAsync(cancellationToken);
        foreach (var paired in remotes.Concat(peers).DistinctBy(p => p.NodeId))
            found.Add(new ArcanistServer(paired.Name, null, paired.NodeId, ArcanistServerVia.Paired));
        return found;
    }

    /// <summary>Arcanist servers on the local networks: media servers made by Arcanist (this one left out).</summary>
    internal static async Task<IReadOnlyList<ArcanistServer>> DiscoverAsync(MediaServerClient client, string? self, CancellationToken cancellationToken)
    {
        var servers = await client.SearchAsync(TimeSpan.FromSeconds(3), cancellationToken);
        return servers
            .Where(s => s.Manufacturer == DlnaDescriptions.Manufacturer && s.Model == DlnaDescriptions.ModelName)
            .Where(s => self is null || !s.Id.Equals(self, StringComparison.OrdinalIgnoreCase))
            .Select(s => new ArcanistServer(s.Name, new UriBuilder(s.Location.Scheme, s.Location.Host, s.Location.Port).Uri.ToString().TrimEnd('/'),
                null, ArcanistServerVia.Network))
            .DistinctBy(s => s.Url)
            .ToList();
    }

    public async Task<ArcanistSourceAnalysis> AnalyzeAsync(ArcanistSourceInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        var source = await EndpointAsync(input, cancellationToken);
        var manifest = await Client.GetAsync<ExportManifest>(source, "/manifest", cancellationToken);
        var here = await db.Libraries.AsNoTracking().Where(l => l.OwnerUserId == null).Select(l => new { l.Id, l.Name }).ToListAsync(cancellationToken);
        var names = (await db.Users.AsNoTracking().Select(u => u.NormalizedUserName).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        return Analysis(manifest, source, here.Select(l => (l.Id, l.Name)).ToList(), names,
            services.GetRequiredService<Core.Extensions.ExtensionHost>(), services.GetRequiredService<Hosting.FrontendCatalog>());
    }

    internal static ArcanistSourceAnalysis Analysis(
        ExportManifest manifest, ArcanistEndpoint source, IReadOnlyList<(Guid Id, string Name)> libraries, IReadOnlySet<string> userNames,
        Core.Extensions.ExtensionHost extensions, Hosting.FrontendCatalog players)
    {
        if (manifest.Format > ExportApi.Format)
            throw new ArcanistException.Conflict(ErrorCodes.MigrationSourceUnsupported);
        return new ArcanistSourceAnalysis(
            manifest.ServerName,
            manifest.Version,
            source.Ticket is not null,
            manifest.Libraries.Select(l => new ArcanistSourceLibrary(l.Id, l.Name, l.VisibleToAll, l.Files, l.Bytes, l.Tracks,
                libraries.FirstOrDefault(h => string.Equals(TextNormalizer.Normalize(h.Name), TextNormalizer.Normalize(l.Name), StringComparison.Ordinal)) is
                    { Id: var same } && same != Guid.Empty ? same : null)).ToList(),
            manifest.Users.Select(u => new ArcanistSourceUser(u.Id, u.UserName, u.DisplayName, u.Role, u.IsDisabled, u.HasPassword, u.SecondFactor,
                userNames.Contains(TextNormalizer.Normalize(u.UserName)))).ToList(),
            manifest.Extensions.Select(e =>
            {
                var installed = extensions.Find(e.Id);
                return new ArcanistSourceComponent(e.Id, e.Name, e.Version, e.Enabled, e.BuiltIn, e.OpenSource, installed is not null,
                    installed is { BuiltIn: true } || !e.BuiltIn && e.Package);
            }).ToList(),
            manifest.Players.Select(p =>
            {
                var installed = players.Find(p.Id);
                return new ArcanistSourceComponent(p.Id, p.Name, p.Version, p.Enabled, p.BuiltIn, p.OpenSource, installed is not null,
                    installed is not null || !p.BuiltIn && p.Package);
            }).ToList(),
            manifest.DefaultPlayer);
    }

    public async Task<ArcanistImportJob> StartAsync(StartArcanistImportInput input, CancellationToken cancellationToken)
    {
        var actor = guard.Admin();
        guard.RecentAuthentication();
        var source = await EndpointAsync(input.Source, cancellationToken);
        var manifest = await Client.GetAsync<ExportManifest>(source, "/manifest", cancellationToken);
        if (manifest.Format > ExportApi.Format)
            throw new ArcanistException.Conflict(ErrorCodes.MigrationSourceUnsupported);

        var map = await MapLibrariesAsync(input.Libraries ?? [], manifest, cancellationToken);
        var credentials = input.CopyPasswords && (input.Users ?? []).Count > 0
            ? await Client.GetAsync<List<ExportCredential>>(source, "/credentials", cancellationToken)
            : [];
        var import = await RecordAsync(db, source, manifest, input, map.Map, map.Created, credentials, null, actor.UserId,
            time.GetUtcNow().UtcDateTime, Protector(services), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        accessCache.InvalidateAll();
        Worker.Wake();
        logger.LogInformation("{User} started a migration from {Source}", actor.UserName, manifest.ServerName);
        return (await ListAsync([import.Id], cancellationToken))[0];
    }

    public async Task<IReadOnlyList<ArcanistImportJob>> ListAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        var ids = await db.ArcanistImports.AsNoTracking().OrderByDescending(i => i.CreatedAt).Take(20).Select(i => i.Id).ToListAsync(cancellationToken);
        return await ListAsync(ids, cancellationToken);
    }

    public async Task<ArcanistImportJob> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        guard.Admin();
        var import = await db.ArcanistImports.SingleOrDefaultAsync(i => i.Id == id, cancellationToken) ?? throw new ArcanistException.NotFound();
        if (!Active(import.Status))
            throw new ArcanistException.Conflict(ErrorCodes.ImportNotActive);
        if (!Worker.Cancel(id))
        {
            import.Status = ArcanistImportStatus.Cancelled;
            import.FinishedAt = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            for (var i = 0; i < 50 && await db.ArcanistImports.AsNoTracking().AnyAsync(x => x.Id == id && x.FinishedAt == null, cancellationToken); i++)
                await Task.Delay(100, cancellationToken);
        }

        return (await ListAsync([id], cancellationToken))[0];
    }

    /// <summary>Goes on with a migration that stopped, or copies again the files that failed.</summary>
    public async Task<ArcanistImportJob> RetryAsync(Guid id, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        var import = await db.ArcanistImports.SingleOrDefaultAsync(i => i.Id == id, cancellationToken) ?? throw new ArcanistException.NotFound();
        if (Active(import.Status))
            throw new ArcanistException.Conflict(ErrorCodes.ImportRunning);
        await db.ArcanistImportFiles.Where(f => f.ImportId == id && f.Status == ArcanistImportFileStatus.Failed)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Status, ArcanistImportFileStatus.Pending).SetProperty(f => f.Reason, (string?)null), cancellationToken);
        import.FilesFailed = 0;
        import.Status = ArcanistImportStatus.Queued;
        import.ErrorCode = null;
        import.FinishedAt = null;
        await db.SaveChangesAsync(cancellationToken);
        Worker.Wake();
        return (await ListAsync([id], cancellationToken))[0];
    }

    /// <summary>Removes a finished migration from the history; what it brought stays.</summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        guard.Admin();
        var import = await db.ArcanistImports.SingleOrDefaultAsync(i => i.Id == id, cancellationToken) ?? throw new ArcanistException.NotFound();
        if (Active(import.Status))
            throw new ArcanistException.Conflict(ErrorCodes.ImportRunning);
        db.ArcanistImports.Remove(import);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Removes every finished migration from the history; returns how many.</summary>
    internal static Task<int> ClearAsync(ArcanistDbContext db, CancellationToken cancellationToken) =>
        db.ArcanistImports.Where(i => i.Status == ArcanistImportStatus.Completed || i.Status == ArcanistImportStatus.Failed
                                      || i.Status == ArcanistImportStatus.Cancelled)
            .ExecuteDeleteAsync(cancellationToken);

    internal static bool Active(ArcanistImportStatus status) =>
        status is not (ArcanistImportStatus.Completed or ArcanistImportStatus.Failed or ArcanistImportStatus.Cancelled);

    /// <summary>
    /// Where the source is. A P2P link (or a paired node) turns the P2P network on, as reaching it needs the node.
    /// </summary>
    private async Task<ArcanistEndpoint> EndpointAsync(ArcanistSourceInput? input, CancellationToken cancellationToken)
    {
        var endpoint = ParseSource(input, nodeId => db.P2PRemotes.AsNoTracking().Where(r => r.NodeId == nodeId).Select(r => r.Ticket).FirstOrDefault());
        if (endpoint.Ticket is not null)
            await EnsureP2PAsync(services, setupPending: false, cancellationToken);
        return endpoint;
    }

    /// <summary>The source as given: errors on <c>source.url</c>, <c>source.link</c> or <c>source.code</c>.</summary>
    internal static ArcanistEndpoint ParseSource(ArcanistSourceInput? input, Func<string, string?>? knownTicket)
    {
        if (input is null)
            throw ArcanistException.Field("source", ErrorCodes.Required);
        if (!string.IsNullOrWhiteSpace(input.Link))
        {
            return P2PLinks.Parse(input.Link) is { Migrate: true } link
                ? new ArcanistEndpoint(null, link.Ticket, link.Token)
                : throw ArcanistException.Field("source.link", ErrorCodes.InvalidFormat);
        }

        if (string.IsNullOrWhiteSpace(input.Code))
            throw ArcanistException.Field("source.code", ErrorCodes.Required);
        var code = input.Code.Trim();
        if (!string.IsNullOrWhiteSpace(input.NodeId))
        {
            var stored = knownTicket?.Invoke(input.NodeId.Trim());
            var ticket = stored is null ? new NodeTicket(input.NodeId.Trim()) : JsonSerializer.Deserialize<NodeTicket>(stored, NativeP2P.Json)!;
            return new ArcanistEndpoint(null, ticket, code);
        }

        return ArcanistSourceClient.ParseUrl(input.Url) is { } url
            ? new ArcanistEndpoint(url, null, code)
            : throw ArcanistException.Field("source.url", string.IsNullOrWhiteSpace(input.Url) ? ErrorCodes.Required : ErrorCodes.InvalidFormat);
    }

    /// <summary>Turns the P2P network on (only kept until setup ends when it is pending) and waits for the node.</summary>
    internal static async Task EnsureP2PAsync(IServiceProvider services, bool setupPending, CancellationToken cancellationToken)
    {
        if (!NativeP2P.IsAvailable)
            throw new ArcanistException.Conflict(ErrorCodes.P2PUnavailable);
        var settings = services.GetRequiredService<P2PSettings>();
        var node = services.GetRequiredService<P2PNode>();
        var config = await settings.GetAsync(cancellationToken);
        if (!config.Enabled)
        {
            if (setupPending)
                settings.UseUntilSetup(config with { Enabled = true });
            else
                await settings.SetAsync(config with { Enabled = true }, cancellationToken);
        }

        if (!node.IsRunning)
            await node.ApplyNowAsync(cancellationToken);
        if (!node.IsRunning)
            throw new ArcanistException.Conflict(ErrorCodes.P2PUnavailable);
    }

    /// <summary>The library of this server each chosen library goes to, creating the new ones (one per folder).</summary>
    private async Task<(Dictionary<Guid, Guid> Map, int Created)> MapLibrariesAsync(
        IReadOnlyList<ArcanistLibraryChoice> choices, ExportManifest manifest, CancellationToken cancellationToken)
    {
        var map = new Dictionary<Guid, Guid>();
        var byFolder = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var created = 0;
        for (var i = 0; i < choices.Count; i++)
        {
            var choice = choices[i];
            var library = manifest.Libraries.FirstOrDefault(l => l.Id == choice.SourceLibraryId);
            if (library is null)
                throw ArcanistException.Field($"libraries[{i}].sourceLibraryId", ErrorCodes.NotFound);
            if (map.ContainsKey(library.Id))
                continue;

            if (choice.LibraryId is { } existing)
            {
                var local = await db.LibrarySources.AsNoTracking()
                    .AnyAsync(s => s.LibraryId == existing && s.Kind == LibrarySourceKind.Local && s.Library.OwnerUserId == null, cancellationToken);
                if (!local)
                    throw ArcanistException.Field($"libraries[{i}].libraryId", ErrorCodes.NotFound);
                map[library.Id] = existing;
                continue;
            }

            if (string.IsNullOrWhiteSpace(choice.NewLibraryPath))
                throw ArcanistException.Field($"libraries[{i}].libraryId", ErrorCodes.Required);
            if (byFolder.TryGetValue(choice.NewLibraryPath.Trim(), out var same))
            {
                map[library.Id] = same;
                continue;
            }

            Libraries.Library made;
            try
            {
                made = await libraries.CreateAsync(new CreateLibraryInput(
                    string.IsNullOrWhiteSpace(choice.NewLibraryName) ? library.Name : choice.NewLibraryName.Trim(), library.VisibleToAll,
                    library.ScanIntervalMinutes ?? 24 * 60, [new LibrarySourceInput(choice.NewLibraryPath.Trim())]), cancellationToken);
            }
            catch (ArcanistException.Invalid invalid)
            {
                var field = invalid.Errors.ToDictionary().Keys.FirstOrDefault() ?? "";
                throw ArcanistException.Field($"libraries[{i}].{(field.StartsWith("sources", StringComparison.Ordinal) ? "newLibraryPath" : "newLibraryName")}",
                    invalid.Errors.ToDictionary().Values.First()[0]);
            }

            if (library.OrganizeTemplate is not null)
            {
                await db.Libraries.Where(l => l.Id == made.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.OrganizeTemplate, library.OrganizeTemplate), cancellationToken);
            }

            byFolder[choice.NewLibraryPath.Trim()] = made.Id;
            map[library.Id] = made.Id;
            created++;
        }

        return (map, created);
    }

    /// <summary>
    /// Adds the migration to <paramref name="db"/> (not saved) with the users it creates or maps; the worker does the
    /// rest. <paramref name="admin"/> (setup's new administrator) takes the place of a user of the same name.
    /// </summary>
    internal static async Task<ArcanistImport> RecordAsync(
        ArcanistDbContext db, ArcanistEndpoint source, ExportManifest manifest, StartArcanistImportInput input, IReadOnlyDictionary<Guid, Guid> libraries,
        int librariesCreated, IReadOnlyList<ExportCredential> credentials, (Guid Id, string UserName)? admin, Guid requestedBy, DateTime now,
        IDataProtector protector, CancellationToken cancellationToken)
    {
        var import = new ArcanistImport
        {
            SourceName = Cut(manifest.ServerName, 200)!,
            SourceVersion = Cut(manifest.Version, 64),
            SourceUrl = source.BaseUrl?.ToString(),
            SourceTicket = source.Ticket is null ? null : JsonSerializer.Serialize(source.Ticket, NativeP2P.Json),
            ProtectedCode = protector.Protect(source.Code),
            Plan = JsonSerializer.Serialize(new ArcanistImportPlan((input.Users ?? []).Distinct().ToList(), input.CopyPasswords, input.Permissions,
                input.UserData, input.Settings, input.Extensions), JsonSerializerOptions.Web),
            LibraryMap = JsonSerializer.Serialize(libraries),
            Status = ArcanistImportStatus.Queued,
            RequestedByUserId = requestedBy,
            CreatedAt = now,
            LibrariesCreated = librariesCreated,
        };

        var users = new Dictionary<Guid, Guid>();
        var passwords = credentials.ToDictionary(c => c.UserId, c => c.PasswordHash);
        foreach (var id in (input.Users ?? []).Distinct())
        {
            var user = manifest.Users.FirstOrDefault(u => u.Id == id);
            if (user is null)
                continue;
            var normalized = TextNormalizer.Normalize(user.UserName);
            if (admin is { } setupAdmin && TextNormalizer.Normalize(setupAdmin.UserName) == normalized)
            {
                users[user.Id] = setupAdmin.Id;
                import.UsersMapped++;
                continue;
            }

            var existing = db.Users.Local.FirstOrDefault(u => u.NormalizedUserName == normalized)?.Id
                           ?? await db.Users.Where(u => u.NormalizedUserName == normalized).Select(u => (Guid?)u.Id).SingleOrDefaultAsync(cancellationToken);
            if (existing is { } same)
            {
                users[user.Id] = same;
                import.UsersMapped++;
                continue;
            }

            var errors = new ValidationErrors();
            UserRules.ValidateUserName(user.UserName, "userName", errors);
            if (!errors.IsEmpty)
                continue;

            var created = new User
            {
                UserName = user.UserName.Trim(),
                NormalizedUserName = normalized,
                DisplayName = Cut(user.DisplayName, 200),
                Role = input.Permissions ? user.Role : UserRole.User,
                Permissions = input.Permissions ? PermissionMapping.ToFlags(user.Permissions) : Permissions.Download,
                // Hashes are ASP.NET Core Identity's, as here: they verify as they are. Second factors are never
                // brought: when Arcanist has them, a user who had one pairs it again at their first sign-in.
                PasswordHash = input.CopyPasswords ? passwords.GetValueOrDefault(user.Id) ?? "" : "",
                IsDisabled = user.IsDisabled,
                CreatedAt = now,
            };
            if (input.Permissions && user.Role != UserRole.Admin)
            {
                var readOnly = user.ReadOnlyLibraryIds.ToHashSet();
                created.LibraryAccess.AddRange(user.LibraryIds.Where(libraries.ContainsKey)
                    .GroupBy(l => libraries[l])
                    .Select(g => new LibraryAccess { UserId = created.Id, LibraryId = g.Key, ReadOnly = g.All(readOnly.Contains) }));
            }

            db.Users.Add(created);
            users[user.Id] = created.Id;
            import.UsersCreated++;
        }

        import.UserMap = JsonSerializer.Serialize(users);
        db.ArcanistImports.Add(import);
        return import;
    }

    private static string? Cut(string? text, int length) => text is { } value && value.Length > length ? value[..length] : text;

    private async Task<IReadOnlyList<ArcanistImportJob>> ListAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var imports = await db.ArcanistImports.AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync(cancellationToken);
        var bytes = await db.ArcanistImportFiles.AsNoTracking().Where(f => ids.Contains(f.ImportId))
            .GroupBy(f => f.ImportId).Select(g => new { g.Key, Bytes = g.Sum(f => f.Size) })
            .ToDictionaryAsync(g => g.Key, g => g.Bytes, cancellationToken);
        var failures = await db.ArcanistImportFiles.AsNoTracking()
            .Where(f => ids.Contains(f.ImportId) && f.Status == ArcanistImportFileStatus.Failed)
            .OrderBy(f => f.Path).Select(f => new { f.ImportId, f.Path, f.Reason })
            .ToListAsync(cancellationToken);
        var libraryIds = imports.SelectMany(LibraryIds).Distinct().ToList();
        var names = await db.Libraries.AsNoTracking().Where(l => libraryIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);

        return imports.OrderByDescending(i => i.CreatedAt).Select(i => new ArcanistImportJob(
                i.Id, i.SourceName, i.SourceVersion, i.Status, i.CreatedAt, i.FinishedAt, i.ErrorCode,
                LibraryIds(i).Distinct().Select(id => names.GetValueOrDefault(id)).OfType<string>().ToList(),
                i.UsersCreated, i.UsersMapped, i.LibrariesCreated, i.SettingsApplied, i.ExtensionsInstalled, i.PlayersInstalled,
                i.FilesTotal, i.FilesCopied, i.FilesPresent, i.FilesFailed, bytes.GetValueOrDefault(i.Id), i.CopiedBytes,
                i.Ratings, i.PlayCounts, i.Plays, i.Playlists, i.Wishes, i.Unmatched,
                !Active(i.Status) && (i.FilesFailed > 0 || i.Status is ArcanistImportStatus.Failed or ArcanistImportStatus.Cancelled),
                failures.Where(f => f.ImportId == i.Id).Take(50).Select(f => new ArcanistImportFailure(f.Path, f.Reason ?? ErrorCodes.DownloadFailed)).ToList()))
            .ToList();
    }

    private static IEnumerable<Guid> LibraryIds(ArcanistImport import) =>
        (JsonSerializer.Deserialize<Dictionary<Guid, Guid>>(import.LibraryMap) ?? []).Values;

    internal static IDataProtector Protector(IServiceProvider services) =>
        services.GetRequiredService<IDataProtectionProvider>().CreateProtector(CodePurpose);
}
