using System.Globalization;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Data.Search;
using Microsoft.EntityFrameworkCore;
using PlaylistEntity = Arcanist.Core.Data.Entities.Playlist;
using PlaylistEntryEntity = Arcanist.Core.Data.Entities.PlaylistEntry;

namespace Arcanist.Core.Application.Playlists;

/// <summary>
/// Playlists. A playlist belongs to its owner, who can make it public (every user sees it and can copy it) and
/// invite collaborators (who add, remove and reorder tracks). Playlists the user cannot see are reported as not found.
/// Tracks from libraries a viewer cannot access stay in the playlist but are not shown to them.
/// </summary>
public sealed class PlaylistService(ArcanistDbContext db, AccessGuard guard, CatalogService catalog, TimeProvider time)
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int MaxEntries = 10_000;
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 1000;

    /// <summary>The user's playlists, those shared with them and public ones, most recently changed first.</summary>
    public async Task<IReadOnlyList<Playlist>> ListAsync(CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var playlists = await Visible(access).AsNoTracking().OrderByDescending(p => p.UpdatedAt).ToListAsync(cancellationToken);
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
            CreatedAt = now,
            UpdatedAt = now,
        };
        playlist.Entries.AddRange(trackIds.Select((trackId, i) => new PlaylistEntryEntity
        {
            PlaylistId = playlist.Id, TrackId = trackId, Position = i, AddedAt = now, AddedByUserId = access.UserId,
        }));
        db.Playlists.Add(playlist);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(playlist.Id, cancellationToken);
    }

    /// <summary>Renames, describes and shares the playlist. Owner only.</summary>
    public async Task<Playlist> UpdateAsync(Guid id, UpdatePlaylistInput input, CancellationToken cancellationToken)
    {
        var (_, playlist) = await FindAsync(id, PlaylistRole.Owner, cancellationToken);
        var errors = new ValidationErrors();
        var name = ValidateNames(input.Name, input.Description, errors);
        errors.ThrowIfAny();

        playlist.Name = name;
        playlist.NormalizedName = TextNormalizer.Normalize(name);
        playlist.Description = Clean(input.Description);
        playlist.SearchText = FullTextSearch.Text(name, input.Description);
        playlist.IsPublic = input.IsPublic;
        playlist.UpdatedAt = Now();
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Deletes the playlist. Owner only.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var (_, playlist) = await FindAsync(id, PlaylistRole.Owner, cancellationToken);
        db.Playlists.Remove(playlist);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Makes a playlist of the user's own with the tracks of one they can see (that they can play).</summary>
    public async Task<Playlist> CopyAsync(Guid id, string? name, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var source = await Visible(access).AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
                     ?? throw new ArcanistException.NotFound();
        var visible = catalog.Visible(access);
        var trackIds = await db.PlaylistEntries.Where(e => e.PlaylistId == id && visible.Any(t => t.Id == e.TrackId))
            .OrderBy(e => e.Position).Select(e => e.TrackId).ToListAsync(cancellationToken);

        return await CreateAsync(new CreatePlaylistInput(string.IsNullOrWhiteSpace(name) ? source.Name : name, source.Description, false, trackIds), cancellationToken);
    }

    /// <summary>Adds tracks (owner or collaborators), at the end or at <see cref="AddPlaylistTracksInput.Position"/>.</summary>
    public async Task<Playlist> AddTracksAsync(Guid id, AddPlaylistTracksInput input, CancellationToken cancellationToken)
    {
        var (access, playlist) = await FindAsync(id, PlaylistRole.Collaborator, cancellationToken);
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
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Removes entries (owner or collaborators).</summary>
    public async Task<Playlist> RemoveEntriesAsync(Guid id, IReadOnlyList<Guid> entryIds, CancellationToken cancellationToken)
    {
        var (_, playlist) = await FindAsync(id, PlaylistRole.Collaborator, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.PlaylistEntries.Where(e => e.PlaylistId == id && entryIds.Contains(e.Id)).ExecuteDeleteAsync(cancellationToken);
        await RenumberAsync(id, keep: null, cancellationToken);
        playlist.UpdatedAt = Now();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Moves entries together to a position (owner or collaborators), keeping their order.</summary>
    public async Task<Playlist> MoveEntriesAsync(Guid id, MovePlaylistEntriesInput input, CancellationToken cancellationToken)
    {
        var (_, playlist) = await FindAsync(id, PlaylistRole.Collaborator, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await RenumberAsync(id, (input.EntryIds, input.Position), cancellationToken);
        playlist.UpdatedAt = Now();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Replaces the users who can edit the playlist's tracks. Owner only.</summary>
    public async Task<Playlist> SetCollaboratorsAsync(Guid id, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken)
    {
        var (access, playlist) = await FindAsync(id, PlaylistRole.Owner, cancellationToken);
        var wanted = userIds.Where(u => u != access.UserId).Distinct().ToList();
        var existing = await db.Users.Where(u => wanted.Contains(u.Id) && !u.IsDisabled).Select(u => u.Id).ToListAsync(cancellationToken);
        if (existing.Count != wanted.Count)
            throw ArcanistException.Field("userIds", ErrorCodes.NotFound);

        await db.PlaylistCollaborators.Where(c => c.PlaylistId == id).ExecuteDeleteAsync(cancellationToken);
        db.PlaylistCollaborators.AddRange(wanted.Select(user => new PlaylistCollaborator { PlaylistId = id, UserId = user, AddedAt = Now() }));
        playlist.UpdatedAt = Now();
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Active users of the server, to invite as collaborators.</summary>
    public async Task<IReadOnlyList<UserReference>> ShareableUsersAsync(CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var users = await db.Users.AsNoTracking().Where(u => !u.IsDisabled && u.Id != access.UserId)
            .OrderBy(u => u.NormalizedUserName)
            .Select(u => new { u.Id, u.UserName, u.DisplayName })
            .ToListAsync(cancellationToken);
        return users.Select(u => new UserReference(u.Id, u.DisplayName ?? u.UserName)).ToList();
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
        var actual = RoleOf(playlist, access.UserId);
        return actual <= role ? (access, playlist) : throw new ArcanistException.Forbidden();
    }

    private static PlaylistRole RoleOf(PlaylistEntity playlist, Guid userId) =>
        playlist.OwnerId == userId ? PlaylistRole.Owner
        : playlist.Collaborators.Any(c => c.UserId == userId) ? PlaylistRole.Collaborator
        : PlaylistRole.Viewer;

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

        return playlists.Select(p =>
        {
            var members = collaborators[p.Id].ToList();
            var role = p.OwnerId == access.UserId ? PlaylistRole.Owner
                : members.Any(c => c.UserId == access.UserId) ? PlaylistRole.Collaborator
                : PlaylistRole.Viewer;
            return new Playlist(
                p.Id,
                p.Name,
                p.Description,
                p.IsPublic,
                users.GetValueOrDefault(p.OwnerId) ?? new UserReference(p.OwnerId, ""),
                role,
                members.Select(c => users.GetValueOrDefault(c.UserId)).OfType<UserReference>().OrderBy(u => u.Name).ToList(),
                totals.TryGetValue(p.Id, out var total) ? total.Count : 0,
                total?.Duration ?? 0,
                covers.GetValueOrDefault(p.Id) ?? [],
                p.CreatedAt,
                p.UpdatedAt);
        }).ToList();
    }

    private async Task<Dictionary<Guid, UserReference>> UsersAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken) =>
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
