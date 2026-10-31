using System.Globalization;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Sharing;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Data.Search;
using Microsoft.EntityFrameworkCore;
using PlaylistEntity = Arcanist.Core.Data.Entities.Playlist;
using PlaylistEntryEntity = Arcanist.Core.Data.Entities.PlaylistEntry;

namespace Arcanist.Core.Application.Playlists;

/// <summary>
/// Playlists. A playlist belongs to its owner, who can make it public (every user sees it and can copy it), share it
/// with users to listen, edit (add, remove and reorder tracks) or manage it (also rename and share it), and give it a
/// public link (when the server allows it). Playlists the user cannot see are reported as not found. Tracks from
/// libraries a viewer cannot access stay in the playlist but are not shown to them.
/// </summary>
public sealed class PlaylistService(
    ArcanistDbContext db,
    AccessGuard guard,
    CatalogService catalog,
    PlaylistSignals signals,
    SharingSettings sharing,
    SharingLinks links,
    TimeProvider time)
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int MaxEntries = 10_000;
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 1000;
    public const int MaxMembers = 500;

    /// <summary>The user's playlists, those shared with them and public ones, most recently changed first.</summary>
    /// <param name="changedSince">Only those changed after this (name, entries, sharing). Deleted ones are not listed:
    /// the whole list, without it, says which are left.</param>
    public async Task<IReadOnlyList<Playlist>> ListAsync(CancellationToken cancellationToken, DateTime? changedSince = null)
    {
        var access = guard.Authenticated();
        var visible = Visible(access).AsNoTracking();
        if (changedSince is { } since)
        {
            since = since.ToUniversalTime();
            visible = visible.Where(p => p.UpdatedAt > since);
        }

        var playlists = await visible.OrderByDescending(p => p.UpdatedAt).ToListAsync(cancellationToken);
        return await ToModelsAsync(access, playlists, cancellationToken);
    }

    public async Task<Playlist> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var playlist = await Visible(access).AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
                       ?? throw new ArcanistException.NotFound();
        return (await ToModelsAsync(access, [playlist], cancellationToken))[0];
    }

    /// <summary>The playlist's tracks in order, page by page.</summary>
    public async Task<PlaylistEntryPage> EntriesAsync(Guid id, string? after, int? first, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        if (!await Visible(access).AnyAsync(p => p.Id == id, cancellationToken))
            throw new ArcanistException.NotFound();
        return await PageAsync(access, id, after, first, cancellationToken);
    }

    /// <summary>A page of entries as <paramref name="access"/> sees them (their tracks, the others left out).</summary>
    internal async Task<PlaylistEntryPage> PageAsync(UserAccess access, Guid id, string? after, int? first, CancellationToken cancellationToken)
    {
        var visible = catalog.Visible(access);
        var entries = db.PlaylistEntries.AsNoTracking().Where(e => e.PlaylistId == id && visible.Any(t => t.Id == e.TrackId));
        var total = await entries.CountAsync(cancellationToken);
        if (after is not null)
        {
            if (!int.TryParse(after, NumberStyles.None, CultureInfo.InvariantCulture, out var position))
                throw ArcanistException.Field("after", ErrorCodes.InvalidFormat);
            entries = entries.Where(e => e.Position > position);
        }

        var size = Math.Clamp(first ?? DefaultPageSize, 1, MaxPageSize);
        var page = await entries.OrderBy(e => e.Position).Take(size + 1).ToListAsync(cancellationToken);
        var next = page.Count > size ? page[size - 1].Position.ToString(CultureInfo.InvariantCulture) : null;
        page = page.Take(size).ToList();

        var tracks = await catalog.VisibleTracksAsync(access, page.Select(e => e.TrackId).Distinct().ToList(), cancellationToken);
        var users = await UsersAsync(page.Where(e => e.AddedByUserId != null).Select(e => e.AddedByUserId!.Value).Distinct().ToList(), cancellationToken);
        return new PlaylistEntryPage(
            page.Where(e => tracks.ContainsKey(e.TrackId))
                .Select(e => new PlaylistEntry(e.Id, e.Position, tracks[e.TrackId], e.AddedAt,
                    e.AddedByUserId is { } user ? users.GetValueOrDefault(user) : null))
                .ToList(),
            next,
            total);
    }

    public async Task<Playlist> CreateAsync(CreatePlaylistInput input, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var playlist = await NewAsync(access, input, PlaylistKind.Normal, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        Publish(playlist, []);
        return await GetAsync(playlist.Id, cancellationToken);
    }

    /// <summary>A new playlist of <paramref name="access"/>'s, added to the context (not saved).</summary>
    internal async Task<PlaylistEntity> NewAsync(UserAccess access, CreatePlaylistInput input, PlaylistKind kind, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var name = ValidateNames(input.Name, input.Description, errors);
        var trackIds = input.TrackIds ?? [];
        if (trackIds.Count > MaxEntries)
            errors.Add("trackIds", ErrorCodes.PlaylistFull);
        errors.ThrowIfAny();
        await EnsureTracksVisibleAsync(access, trackIds, cancellationToken);

        var now = Now();
        var playlist = new PlaylistEntity
        {
            OwnerId = access.UserId,
            Name = name,
            NormalizedName = TextNormalizer.Normalize(name),
            Description = Clean(input.Description),
            SearchText = FullTextSearch.Text(name, input.Description),
            IsPublic = input.IsPublic,
            Kind = kind,
            CreatedAt = now,
            UpdatedAt = now,
        };
        playlist.Entries.AddRange(trackIds.Select((trackId, i) => new PlaylistEntryEntity
        {
            PlaylistId = playlist.Id, TrackId = trackId, Position = i, AddedAt = now, AddedByUserId = access.UserId,
        }));
        db.Playlists.Add(playlist);
        return playlist;
    }

    /// <summary>Renames, describes and makes it public or not. Owner or managers.</summary>
    public async Task<Playlist> UpdateAsync(Guid id, UpdatePlaylistInput input, CancellationToken cancellationToken)
    {
        var (_, playlist) = await FindAsync(id, PlaylistRole.Manager, cancellationToken);
        var errors = new ValidationErrors();
        var name = ValidateNames(input.Name, input.Description, errors);
        errors.ThrowIfAny();

        var members = MemberIds(playlist);
        playlist.Name = name;
        playlist.NormalizedName = TextNormalizer.Normalize(name);
        playlist.Description = Clean(input.Description);
        playlist.SearchText = FullTextSearch.Text(name, input.Description);
        var wasPublic = playlist.IsPublic;
        playlist.IsPublic = input.IsPublic;
        playlist.UpdatedAt = Now();
        await db.SaveChangesAsync(cancellationToken);
        Publish(playlist, members, wasPublic);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Deletes the playlist. Owner only.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var (_, playlist) = await FindAsync(id, PlaylistRole.Owner, cancellationToken);
        if (playlist.Kind == PlaylistKind.Jam && await db.JamSessions.AnyAsync(j => j.PlaylistId == id && j.EndedAt == null, cancellationToken))
            throw new ArcanistException.Conflict(ErrorCodes.PlaylistIsJam);
        var members = MemberIds(playlist);
        db.Playlists.Remove(playlist);
        await db.SaveChangesAsync(cancellationToken);
        Publish(playlist, members, deleted: true);
    }

    /// <summary>Makes a playlist of the user's own with the tracks of one they can see (that they can play).</summary>
    public async Task<Playlist> CopyAsync(Guid id, string? name, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var source = await Visible(access).AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
                     ?? throw new ArcanistException.NotFound();
        return await CopyFromAsync(access, source, name, cancellationToken);
    }

    /// <summary>Copies a playlist the user reached by its public link.</summary>
    internal async Task<Playlist> CopyFromAsync(UserAccess access, PlaylistEntity source, string? name, CancellationToken cancellationToken)
    {
        var visible = catalog.Visible(access);
        var trackIds = await db.PlaylistEntries.Where(e => e.PlaylistId == source.Id && visible.Any(t => t.Id == e.TrackId))
            .OrderBy(e => e.Position).Select(e => e.TrackId).ToListAsync(cancellationToken);

        return await CreateAsync(new CreatePlaylistInput(string.IsNullOrWhiteSpace(name) ? source.Name : name, source.Description, false, trackIds), cancellationToken);
    }

    /// <summary>Adds tracks (owner or editors), at the end or at <see cref="AddPlaylistTracksInput.Position"/>.</summary>
    public async Task<Playlist> AddTracksAsync(Guid id, AddPlaylistTracksInput input, CancellationToken cancellationToken)
    {
        var (access, playlist) = await FindAsync(id, PlaylistRole.Collaborator, cancellationToken);
        RejectJam(playlist);
        if (input.TrackIds is not { Count: > 0 })
            throw ArcanistException.Field("trackIds", ErrorCodes.Required);
        await EnsureTracksVisibleAsync(access, input.TrackIds, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var count = await db.PlaylistEntries.CountAsync(e => e.PlaylistId == id, cancellationToken);
        if (count + input.TrackIds.Count > MaxEntries)
            throw ArcanistException.Field("trackIds", ErrorCodes.PlaylistFull);

        var position = Math.Clamp(input.Position ?? count, 0, count);
        await db.PlaylistEntries.Where(e => e.PlaylistId == id && e.Position >= position)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Position, e => e.Position + input.TrackIds.Count), cancellationToken);

        var now = Now();
        db.PlaylistEntries.AddRange(input.TrackIds.Select((trackId, i) => new PlaylistEntryEntity
        {
            PlaylistId = id, TrackId = trackId, Position = position + i, AddedAt = now, AddedByUserId = access.UserId,
        }));
        playlist.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        Publish(playlist, MemberIds(playlist));
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Removes entries (owner or editors).</summary>
    public async Task<Playlist> RemoveEntriesAsync(Guid id, IReadOnlyList<Guid> entryIds, CancellationToken cancellationToken)
    {
        var (_, playlist) = await FindAsync(id, PlaylistRole.Collaborator, cancellationToken);
        RejectJam(playlist);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.PlaylistEntries.Where(e => e.PlaylistId == id && entryIds.Contains(e.Id)).ExecuteDeleteAsync(cancellationToken);
        await RenumberAsync(id, keep: null, cancellationToken);
        playlist.UpdatedAt = Now();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        Publish(playlist, MemberIds(playlist));
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Moves entries together to a position (owner or editors), keeping their order.</summary>
    public async Task<Playlist> MoveEntriesAsync(Guid id, MovePlaylistEntriesInput input, CancellationToken cancellationToken)
    {
        var (_, playlist) = await FindAsync(id, PlaylistRole.Collaborator, cancellationToken);
        RejectJam(playlist);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await RenumberAsync(id, (input.EntryIds, input.Position), cancellationToken);
        playlist.UpdatedAt = Now();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        Publish(playlist, MemberIds(playlist));
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Replaces the users who can edit the playlist's tracks (owner or managers). Kept from before members had roles:
    /// those listed edit it (or still manage it), editors and managers left out are removed, listeners stay.
    /// </summary>
    public async Task<Playlist> SetCollaboratorsAsync(Guid id, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken)
    {
        var (_, playlist) = await FindAsync(id, PlaylistRole.Manager, cancellationToken);
        var wanted = userIds.ToHashSet();
        var members = playlist.Collaborators.Where(c => c.Role == PlaylistMemberRole.Listen && !wanted.Contains(c.UserId))
            .Select(c => new PlaylistMemberInput(c.UserId, PlaylistShareRole.Listen))
            .Concat(wanted.Select(user => new PlaylistMemberInput(user,
                playlist.Collaborators.FirstOrDefault(c => c.UserId == user)?.Role == PlaylistMemberRole.Manage ? PlaylistShareRole.Manage : PlaylistShareRole.Edit)))
            .ToList();
        return await ReplaceMembersAsync(playlist, members, cancellationToken);
    }

    /// <summary>Replaces the users the playlist is shared with and what each can do. Owner or managers.</summary>
    public async Task<Playlist> SetMembersAsync(Guid id, IReadOnlyList<PlaylistMemberInput> members, CancellationToken cancellationToken)
    {
        var (_, playlist) = await FindAsync(id, PlaylistRole.Manager, cancellationToken);
        return await ReplaceMembersAsync(playlist, members, cancellationToken);
    }

    private async Task<Playlist> ReplaceMembersAsync(PlaylistEntity playlist, IReadOnlyList<PlaylistMemberInput> members, CancellationToken cancellationToken)
    {
        // The owner is never a member of their own playlist.
        var wanted = members.Where(m => m.UserId != playlist.OwnerId).GroupBy(m => m.UserId).Select(g => g.Last()).ToList();
        if (wanted.Count > MaxMembers)
            throw ArcanistException.Field("members", ErrorCodes.InvalidFormat);
        var ids = wanted.Select(m => m.UserId).ToList();
        var existing = await db.Users.Where(u => ids.Contains(u.Id) && !u.IsDisabled).Select(u => u.Id).ToListAsync(cancellationToken);
        if (existing.Count != ids.Count)
            throw ArcanistException.Field("members", ErrorCodes.NotFound);

        var before = MemberIds(playlist);
        var now = Now();
        foreach (var member in playlist.Collaborators.Where(c => !ids.Contains(c.UserId)).ToList())
            db.PlaylistCollaborators.Remove(member);
        foreach (var member in wanted)
        {
            var role = ToEntity(member.Role);
            if (playlist.Collaborators.FirstOrDefault(c => c.UserId == member.UserId) is { } current)
                current.Role = role;
            else
                db.PlaylistCollaborators.Add(new PlaylistCollaborator { PlaylistId = playlist.Id, UserId = member.UserId, Role = role, AddedAt = now });
        }
        playlist.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        Publish(playlist, before.Concat(ids).ToList());
        return await GetAsync(playlist.Id, cancellationToken);
    }

    /// <summary>
    /// Gives the playlist a public link, or returns the one it has: anyone with it sees the playlist and hears 30-second
    /// previews. Owner or managers, with <see cref="Permissions.ShareLinks"/>, when the server allows public links.
    /// </summary>
    public async Task<Playlist> CreateLinkAsync(Guid id, CancellationToken cancellationToken)
    {
        var (access, playlist) = await FindAsync(id, PlaylistRole.Manager, cancellationToken);
        RejectJam(playlist);
        if (!(await sharing.GetAsync(cancellationToken)).PublicLinks)
            throw new ArcanistException.Conflict(ErrorCodes.PublicLinksDisabled);
        if (!access.Has(Permissions.ShareLinks))
            throw new ArcanistException.Forbidden(ErrorCodes.PublicLinksDisabled);

        if (playlist.LinkToken is null)
        {
            playlist.LinkToken = SharingLinks.NewToken();
            playlist.LinkCreatedAt = Now();
            playlist.UpdatedAt = Now();
            await db.SaveChangesAsync(cancellationToken);
            Publish(playlist, MemberIds(playlist));
        }
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Removes the playlist's public link: it no longer opens. Owner or managers.</summary>
    public async Task<Playlist> RevokeLinkAsync(Guid id, CancellationToken cancellationToken)
    {
        var (_, playlist) = await FindAsync(id, PlaylistRole.Manager, cancellationToken);
        if (playlist.LinkToken is not null)
        {
            playlist.LinkToken = null;
            playlist.LinkCreatedAt = null;
            playlist.UpdatedAt = Now();
            await db.SaveChangesAsync(cancellationToken);
            Publish(playlist, MemberIds(playlist));
        }
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// The changes of the playlists the user sees, or of one of them, as they happen (nothing comes of one they
    /// cannot see).
    /// </summary>
    public IObservable<PlaylistChange> Watch(Guid? playlistId) => signals.For(guard.Authenticated(), playlistId);

    /// <summary>Active users of the server, to share playlists with.</summary>
    public async Task<IReadOnlyList<UserReference>> ShareableUsersAsync(CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var users = await db.Users.AsNoTracking().Where(u => !u.IsDisabled && u.Id != access.UserId)
            .OrderBy(u => u.NormalizedUserName)
            .Select(u => new { u.Id, u.UserName, u.DisplayName })
            .ToListAsync(cancellationToken);
        return users.Select(u => new UserReference(u.Id, u.DisplayName ?? u.UserName)).ToList();
    }

    /// <summary>Tells subscribers a playlist changed (also used by the Jukebox Jams).</summary>
    internal void Publish(PlaylistEntity playlist, IReadOnlyCollection<Guid> members, bool? wasPublic = null, bool deleted = false) =>
        signals.Publish(new PlaylistSignal(new PlaylistChange(playlist.Id, deleted, Now()), playlist.OwnerId,
            members.Concat(MemberIds(playlist)).ToHashSet(), playlist.IsPublic || wasPublic == true));

    private static List<Guid> MemberIds(PlaylistEntity playlist) => playlist.Collaborators.Select(c => c.UserId).ToList();

    private static void RejectJam(PlaylistEntity playlist)
    {
        if (playlist.Kind == PlaylistKind.Jam)
            throw new ArcanistException.Conflict(ErrorCodes.PlaylistIsJam);
    }

    /// <summary>Rewrites positions 0…n-1, optionally moving some entries to a position first.</summary>
    private async Task RenumberAsync(Guid id, (IReadOnlyList<Guid> EntryIds, int Position)? keep, CancellationToken cancellationToken)
    {
        var entries = await db.PlaylistEntries.Where(e => e.PlaylistId == id).OrderBy(e => e.Position).ToListAsync(cancellationToken);
        if (keep is { } move)
        {
            var moving = entries.Where(e => move.EntryIds.Contains(e.Id)).ToList();
            if (moving.Count != move.EntryIds.Distinct().Count())
                throw ArcanistException.Field("entryIds", ErrorCodes.NotFound);

            entries = entries.Except(moving).ToList();
            entries.InsertRange(Math.Clamp(move.Position, 0, entries.Count), moving);
        }

        for (var i = 0; i < entries.Count; i++)
            entries[i].Position = i;
    }

    /// <summary>Playlists <paramref name="access"/> can see, for queries built elsewhere.</summary>
    internal IQueryable<PlaylistEntity> VisibleQuery(UserAccess access) => Visible(access);

    /// <summary>Turns playlist rows <paramref name="access"/> can see into contract playlists, in the same order.</summary>
    internal Task<IReadOnlyList<Playlist>> ModelsAsync(UserAccess access, IReadOnlyList<PlaylistEntity> playlists, CancellationToken cancellationToken) =>
        ToModelsAsync(access, playlists, cancellationToken);

    private IQueryable<PlaylistEntity> Visible(UserAccess access) =>
        db.Playlists.Where(p => p.OwnerId == access.UserId || p.IsPublic || p.Collaborators.Any(c => c.UserId == access.UserId));

    /// <summary>A playlist the caller has at least <paramref name="role"/> on; not found when they cannot see it.</summary>
    private async Task<(UserAccess Access, PlaylistEntity Playlist)> FindAsync(Guid id, PlaylistRole role, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var playlist = await Visible(access).Include(p => p.Collaborators).SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
                       ?? throw new ArcanistException.NotFound();
        var actual = RoleOf(playlist.OwnerId, playlist.Collaborators, access.UserId);
        return actual <= role ? (access, playlist) : throw new ArcanistException.Forbidden();
    }

    internal static PlaylistRole RoleOf(Guid ownerId, IEnumerable<PlaylistCollaborator> members, Guid userId) =>
        ownerId == userId ? PlaylistRole.Owner
        : members.FirstOrDefault(c => c.UserId == userId)?.Role switch
        {
            PlaylistMemberRole.Manage => PlaylistRole.Manager,
            PlaylistMemberRole.Edit => PlaylistRole.Collaborator,
            _ => PlaylistRole.Viewer,
        };

    private static PlaylistMemberRole ToEntity(PlaylistShareRole role) => role switch
    {
        PlaylistShareRole.Manage => PlaylistMemberRole.Manage,
        PlaylistShareRole.Edit => PlaylistMemberRole.Edit,
        _ => PlaylistMemberRole.Listen,
    };

    private static PlaylistShareRole ToModel(PlaylistMemberRole role) => role switch
    {
        PlaylistMemberRole.Manage => PlaylistShareRole.Manage,
        PlaylistMemberRole.Edit => PlaylistShareRole.Edit,
        _ => PlaylistShareRole.Listen,
    };

    private async Task EnsureTracksVisibleAsync(UserAccess access, IReadOnlyList<Guid> trackIds, CancellationToken cancellationToken)
    {
        var distinct = trackIds.Distinct().ToList();
        if (distinct.Count == 0)
            return;
        var visible = await catalog.Visible(access).Where(t => distinct.Contains(t.Id)).CountAsync(cancellationToken);
        if (visible != distinct.Count)
            throw ArcanistException.Field("trackIds", ErrorCodes.NotFound);
    }

    private async Task<IReadOnlyList<Playlist>> ToModelsAsync(UserAccess access, IReadOnlyList<PlaylistEntity> playlists, CancellationToken cancellationToken)
    {
        var ids = playlists.Select(p => p.Id).ToList();
        var collaborators = (await db.PlaylistCollaborators.AsNoTracking().Where(c => ids.Contains(c.PlaylistId)).ToListAsync(cancellationToken))
            .ToLookup(c => c.PlaylistId);
        var users = await UsersAsync(playlists.Select(p => p.OwnerId).Concat(collaborators.SelectMany(c => c).Select(c => c.UserId)).Distinct().ToList(), cancellationToken);
        var jams = await db.JamSessions.AsNoTracking().Where(j => ids.Contains(j.PlaylistId) && j.EndedAt == null)
            .ToDictionaryAsync(j => j.PlaylistId, j => j.Code, cancellationToken);
        var publicLinks = (await sharing.GetAsync(cancellationToken)).PublicLinks;

        var visible = catalog.Visible(access);
        var totals = await db.PlaylistEntries.Where(e => ids.Contains(e.PlaylistId) && visible.Any(t => t.Id == e.TrackId))
            .GroupBy(e => e.PlaylistId)
            .Select(g => new { PlaylistId = g.Key, Count = g.Count(), Duration = g.Sum(e => (long)e.Track.DurationMs) })
            .ToDictionaryAsync(t => t.PlaylistId, cancellationToken);

        // Covers of the first tracks' albums; a few rows per playlist are enough to find four different ones.
        var covers = (await db.PlaylistEntries.AsNoTracking()
                .Where(e => ids.Contains(e.PlaylistId) && e.Position < 50 && visible.Any(t => t.Id == e.TrackId))
                .Select(e => new { e.PlaylistId, e.Position, ArtworkId = e.Track.ArtworkId ?? e.Track.Album.ArtworkId })
                .ToListAsync(cancellationToken))
            .Where(c => c.ArtworkId != null)
            .GroupBy(c => c.PlaylistId)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Position).Select(c => c.ArtworkId!.Value).Distinct().Take(4).ToList());

        var models = new List<Playlist>(playlists.Count);
        foreach (var p in playlists)
        {
            var members = collaborators[p.Id].ToList();
            var role = RoleOf(p.OwnerId, members, access.UserId);
            PlaylistLink? link = null;
            if (role <= PlaylistRole.Manager && publicLinks && p.LinkToken is { } token)
                link = new PlaylistLink(token, await links.PlaylistUrlAsync(token, cancellationToken), p.LinkCreatedAt ?? p.CreatedAt);
            models.Add(new Playlist(
                p.Id,
                p.Name,
                p.Description,
                p.IsPublic,
                users.GetValueOrDefault(p.OwnerId) ?? new UserReference(p.OwnerId, ""),
                role,
                members.Where(c => c.Role != PlaylistMemberRole.Listen).Select(c => users.GetValueOrDefault(c.UserId)).OfType<UserReference>().OrderBy(u => u.Name).ToList(),
                totals.TryGetValue(p.Id, out var total) ? total.Count : 0,
                total?.Duration ?? 0,
                covers.GetValueOrDefault(p.Id) ?? [],
                p.CreatedAt,
                p.UpdatedAt,
                p.Kind == PlaylistKind.Jam ? PlaylistType.JukeboxJam : PlaylistType.Normal,
                members.Where(c => users.ContainsKey(c.UserId))
                    .Select(c => new PlaylistMember(users[c.UserId], ToModel(c.Role)))
                    .OrderBy(m => m.User.Name).ToList(),
                link,
                jams.GetValueOrDefault(p.Id)));
        }
        return models;
    }

    internal async Task<Dictionary<Guid, UserReference>> UsersAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken) =>
        (await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.UserName, u.DisplayName }).ToListAsync(cancellationToken))
        .ToDictionary(u => u.Id, u => new UserReference(u.Id, u.DisplayName ?? u.UserName));

    private static string ValidateNames(string? name, string? description, ValidationErrors errors)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
            errors.Add("name", ErrorCodes.Required);
        else if (trimmed.Length > NameMaxLength)
            errors.Add("name", ErrorCodes.InvalidFormat);
        if (description is { Length: > DescriptionMaxLength })
            errors.Add("description", ErrorCodes.InvalidFormat);
        return trimmed;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
