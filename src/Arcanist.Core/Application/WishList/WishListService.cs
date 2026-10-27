using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Track = Arcanist.Core.Application.Catalog.Track;

namespace Arcanist.Core.Application.WishList;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

public enum WishStatus
{
    /// <summary>Not in any library the user can play yet.</summary>
    Wanted,

    /// <summary>It arrived (or was there already): <c>track</c> is the song.</summary>
    Obtained,
}

/// <summary>Whose wishes to list: the user's own, or everyone's (VIEW_ALL_WISHES).</summary>
public enum WishListScope
{
    Mine,
    All,
}

/// <summary>Who made a wish, in everyone's list.</summary>
public sealed record WishOwner(Guid Id, string UserName, string? DisplayName);

/// <summary>A song the user wants.</summary>
/// <param name="AcoustId">The AcoustID the user identified it by.</param>
/// <param name="SourceUrl">The streaming service's page it was added from: to open it in a new tab.</param>
/// <param name="ArtworkId">Its cover, served by the artwork endpoint.</param>
/// <param name="Owner">Who wants it: only in everyone's list (<c>scope: ALL</c>).</param>
/// <param name="Track">The song that fulfilled it, while the user can see it.</param>
/// <param name="MatchedBy">What made that track this song.</param>
/// <param name="FoundWhenAdded">A library had it already when the wish was made.</param>
/// <param name="Seen">The user saw that it arrived (<c>markWishesSeen</c>).</param>
/// <param name="WantsVideo">The user also wants the song's music video.</param>
/// <param name="VideoObtainedAt">When a music video of the song arrived (or was there); null while it is wanted.</param>
/// <param name="VideoSeen">The user saw that the video arrived.</param>
public sealed record Wish(
    Guid Id,
    string Title,
    string Artist,
    string? Album,
    Guid? MusicBrainzRecordingId,
    string? AcoustId,
    int? DurationMs,
    string? Note,
    WishStatus Status,
    DateTime CreatedAt,
    DateTime? ObtainedAt,
    Track? Track,
    TrackMatchKind? MatchedBy,
    bool FoundWhenAdded,
    bool Seen,
    string? SourceUrl = null,
    WishLinkService? SourceService = null,
    Guid? ArtworkId = null,
    WishOwner? Owner = null,
    Guid? TargetLibraryId = null,
    bool WantsVideo = false,
    DateTime? VideoObtainedAt = null,
    bool VideoSeen = false);

/// <param name="Album">Only a copy from this album fulfils it.</param>
/// <param name="DurationMs">Only a copy this long (give or take three seconds) fulfils it.</param>
/// <param name="SourceUrl">The streaming service's page of the song (from <c>resolveWishLink</c>).</param>
/// <param name="ArtworkId">Its cover (from <c>resolveWishLink</c>).</param>
/// <param name="TargetLibraryId">Where the song goes when it is uploaded for this wish (a library the user can upload
/// to); null follows the server's rule for uploads (<c>uploadSettings</c>).</param>
/// <param name="WantsVideo">The user also wants the song's music video: it comes true in two parts, the song and its video. Left out, a new wish does not and a changed one keeps what it had.</param>
public sealed record WishInput(
    string Title,
    string Artist,
    string? Album = null,
    Guid? MusicBrainzRecordingId = null,
    string? AcoustId = null,
    int? DurationMs = null,
    string? Note = null,
    string? SourceUrl = null,
    Guid? ArtworkId = null,
    Guid? TargetLibraryId = null,
    bool? WantsVideo = null);

/// <param name="Items">The wanted ones by the newest wish, then the obtained ones by when they arrived.</param>
/// <param name="Wanted">How many are still wanted.</param>
/// <param name="Unseen">How many arrived and the user has not seen yet.</param>
public sealed record WishListResult(IReadOnlyList<Wish> Items, int Wanted, int Unseen);

/// <summary>What came true of a wish. Part of the API contract.</summary>
public enum WishPart
{
    /// <summary>The song.</summary>
    Audio,

    /// <summary>Its music video.</summary>
    Video,
}

/// <summary>A wish of the user came true: the song (or its music video, <paramref name="Part"/>) is in a library they can play.</summary>
public sealed record WishObtained(Guid WishId, string Title, string Artist, Guid? TrackId, DateTime ObtainedAt, WishPart Part = WishPart.Audio);

/// <summary>
/// Each user's wish list: songs they want, recognised as soon as one arrives in a library they can play (after every
/// scan, so also uploads, imports and copies) by MusicBrainz recording, AcoustID, or title and artist (and album and
/// duration when given). The user is told by the <c>wishObtained</c> subscription, and the list keeps the history.
/// A wish can start from a streaming service's link (<see cref="WishLinkResolver"/>). Everyone's wishes (the server's
/// list) need VIEW_ALL_WISHES. The methods that take a <see cref="UserAccess"/> are for extensions and background work.
/// </summary>
public sealed class WishListService(
    ArcanistDbContext db,
    AccessGuard guard,
    CatalogService catalog,
    WishListSignals signals,
    WishLinkResolver links,
    Libraries.UploadDestinations destinations,
    TimeProvider time)
{
    public const int MaxWishes = 2000;
    public const int TextMaxLength = 300;
    public const int NoteMaxLength = 1000;

    public Task<WishListResult> WishListAsync(WishStatus? status, CancellationToken cancellationToken) =>
        WishListAsync(status, WishListScope.Mine, cancellationToken);

    public Task<WishListResult> WishListAsync(WishStatus? status, WishListScope scope, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        if (scope == WishListScope.All)
            guard.Permission(Permissions.ViewAllWishes);
        return ListAsync(access, scope == WishListScope.All ? null : access.UserId, status, cancellationToken);
    }

    /// <summary>
    /// The wishes of <paramref name="owner"/> (everyone's when null), with the tracks <paramref name="viewer"/> can see.
    /// No permission check: for extensions and background work.
    /// </summary>
    public async Task<WishListResult> ListAsync(UserAccess viewer, Guid? owner, WishStatus? status, CancellationToken cancellationToken)
    {
        var mine = db.WishListItems.AsNoTracking().Where(w => owner == null || w.UserId == owner);
        var wanted = await mine.CountAsync(w => w.ObtainedAt == null, cancellationToken);
        var unseen = await mine.CountAsync(w => (w.ObtainedAt != null && !w.Seen) || (w.VideoObtainedAt != null && !w.VideoSeen), cancellationToken);

        var items = status switch
        {
            WishStatus.Wanted => mine.Where(w => w.ObtainedAt == null),
            WishStatus.Obtained => mine.Where(w => w.ObtainedAt != null),
            _ => mine,
        };
        var rows = await items.ToListAsync(cancellationToken);
        rows = rows.Where(w => w.ObtainedAt == null).OrderByDescending(w => w.CreatedAt)
            .Concat(rows.Where(w => w.ObtainedAt != null).OrderByDescending(w => w.ObtainedAt))
            .ToList();
        return new WishListResult(await ToModelsAsync(viewer, rows, withOwners: owner is null, cancellationToken), wanted, unseen);
    }

    /// <summary>What a streaming service's link says about a song, to fill a wish (external services must be allowed).</summary>
    public Task<WishLinkPreview> ResolveLinkAsync(ResolveWishLinkInput input, CancellationToken cancellationToken)
    {
        guard.Authenticated();
        return links.ResolveAsync(input.Url, cancellationToken);
    }

    /// <summary>Adds a wish; when a library the user can play has the song already, it comes obtained.</summary>
    public Task<Wish> AddAsync(WishInput input, CancellationToken cancellationToken) =>
        AddAsync(guard.Authenticated(), input, cancellationToken);

    /// <summary>Adds a wish for <paramref name="access"/>'s user. No session needed: for extensions and background work.</summary>
    public async Task<Wish> AddAsync(UserAccess access, WishInput input, CancellationToken cancellationToken)
    {
        var wish = new WishListItem { UserId = access.UserId, Title = "", Artist = "", CreatedAt = Now() };
        Apply(wish, input);
        wish.TargetLibraryId = await TargetAsync(access, input.TargetLibraryId, cancellationToken);
        wish.ArtworkId = await ArtworkAsync(input.ArtworkId, cancellationToken);
        if (await db.WishListItems.CountAsync(w => w.UserId == access.UserId, cancellationToken) >= MaxWishes)
            throw ArcanistException.Field("title", ErrorCodes.TooMany);

        db.WishListItems.Add(wish);
        if (await MatchAsync(catalog, access, [wish], Now(), cancellationToken) > 0)
        {
            wish.FoundWhenAdded = true;
            wish.Seen = true;
        }
        if (await MatchVideosAsync(catalog, access, [wish], Now(), cancellationToken) > 0)
            wish.VideoSeen = true;

        await db.SaveChangesAsync(cancellationToken);
        return (await ToModelsAsync(access, [wish], withOwners: false, cancellationToken))[0];
    }

    /// <summary>Changes a wish; a wanted one is looked for again.</summary>
    public async Task<Wish> UpdateAsync(Guid id, WishInput input, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var wish = await db.WishListItems.SingleOrDefaultAsync(w => w.Id == id && w.UserId == access.UserId, cancellationToken)
                   ?? throw new ArcanistException.NotFound();
        Apply(wish, input);
        wish.TargetLibraryId = await TargetAsync(access, input.TargetLibraryId, cancellationToken);
        wish.ArtworkId = await ArtworkAsync(input.ArtworkId, cancellationToken);
        if (wish.ObtainedAt is null && await MatchAsync(catalog, access, [wish], Now(), cancellationToken) > 0)
        {
            wish.FoundWhenAdded = true;
            wish.Seen = true;
        }
        if (await MatchVideosAsync(catalog, access, [wish], Now(), cancellationToken) > 0)
            wish.VideoSeen = true;

        await db.SaveChangesAsync(cancellationToken);
        return (await ToModelsAsync(access, [wish], withOwners: false, cancellationToken))[0];
    }

    public Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken) =>
        RemoveAsync(guard.Authenticated().UserId, id, cancellationToken);

    /// <summary>Removes a wish of <paramref name="user"/>. No permission check: for extensions.</summary>
    public async Task<bool> RemoveAsync(Guid user, Guid id, CancellationToken cancellationToken)
    {
        if (await db.WishListItems.Where(w => w.Id == id && w.UserId == user).ExecuteDeleteAsync(cancellationToken) == 0)
            throw new ArcanistException.NotFound();
        return true;
    }

    /// <summary>The user saw what arrived. Returns how many were new to them.</summary>
    public async Task<int> MarkSeenAsync(CancellationToken cancellationToken)
    {
        var user = guard.Authenticated().UserId;
        var videos = await db.WishListItems.Where(w => w.UserId == user && w.VideoObtainedAt != null && !w.VideoSeen)
            .ExecuteUpdateAsync(w => w.SetProperty(x => x.VideoSeen, true), cancellationToken);
        return videos + await db.WishListItems.Where(w => w.UserId == user && w.ObtainedAt != null && !w.Seen)
            .ExecuteUpdateAsync(w => w.SetProperty(x => x.Seen, true), cancellationToken);
    }

    /// <summary>The user's wishes as they come true, for the <c>wishObtained</c> subscription.</summary>
    public IObservable<WishObtained> Watch() => signals.Of(guard.Authenticated().UserId);

    /// <summary>
    /// Looks for <paramref name="wishes"/> (wanted ones of <paramref name="access"/>) among the tracks the user can
    /// play, and marks those found as obtained (the caller saves). Returns how many were found.
    /// </summary>
    internal static async Task<int> MatchAsync(CatalogService catalog, UserAccess access, IReadOnlyList<WishListItem> wishes, DateTime now,
        CancellationToken cancellationToken)
    {
        var candidates = wishes.Where(w => w.ObtainedAt is null)
            .Select(w => new TrackCandidate(w.Id.ToString("N"), null, w.AcoustId?.ToString(), w.RecordingMbid, w.Title, w.Artist, w.Album, null, null, w.DurationMs))
            .ToList();
        if (candidates.Count == 0)
            return 0;

        var found = await TrackMatchService.FindAsync(catalog.Visible(access), candidates, cancellationToken);
        foreach (var wish in wishes)
        {
            if (!found.TryGetValue(wish.Id.ToString("N"), out var match))
                continue;
            wish.ObtainedAt = now;
            wish.TrackId = match.TrackId;
            wish.MatchedBy = match.By switch
            {
                TrackMatchKind.MusicBrainz => WishMatch.MusicBrainz,
                TrackMatchKind.AcoustId => WishMatch.AcoustId,
                _ => WishMatch.Tags,
            };
        }

        return found.Count;
    }

    /// <summary>
    /// Marks as obtained the music video of <paramref name="wishes"/> that want one, whose song arrived and has a video
    /// <paramref name="access"/>'s user can watch (the caller saves). Returns how many were found.
    /// </summary>
    internal static async Task<int> MatchVideosAsync(CatalogService catalog, UserAccess access, IReadOnlyList<WishListItem> wishes, DateTime now,
        CancellationToken cancellationToken)
    {
        var waiting = wishes.Where(w => w.WantsVideo && w.VideoObtainedAt is null && w.TrackId is not null).ToList();
        if (waiting.Count == 0)
            return 0;
        var trackIds = waiting.Select(w => w.TrackId!.Value).ToList();
        var recordings = await catalog.Visible(access, null).Where(t => trackIds.Contains(t.Id)).Select(t => new { t.Id, t.RecordingId })
            .ToDictionaryAsync(t => t.Id, t => t.RecordingId, cancellationToken);
        var recordingIds = recordings.Values.Distinct().ToList();
        var videos = await catalog.VisibleVideos(access)
            .Where(v => (v.TrackId != null && trackIds.Contains(v.TrackId.Value)) || (v.RecordingId != null && recordingIds.Contains(v.RecordingId.Value)))
            .Select(v => new { v.TrackId, v.RecordingId })
            .ToListAsync(cancellationToken);
        var found = 0;
        foreach (var wish in waiting)
        {
            var recording = recordings.GetValueOrDefault(wish.TrackId!.Value);
            if (!videos.Any(v => v.TrackId == wish.TrackId || (v.RecordingId is { } r && r == recording)))
                continue;
            wish.VideoObtainedAt = now;
            found++;
        }
        return found;
    }

    private static void Apply(WishListItem wish, WishInput input)
    {
        var errors = new ValidationErrors();
        string Text(string field, string? value, bool required, int max = TextMaxLength)
        {
            var trimmed = value?.Trim() ?? "";
            if (required && trimmed.Length == 0)
                errors.Add(field, ErrorCodes.Required);
            else if (trimmed.Length > max)
                errors.Add(field, ErrorCodes.InvalidFormat);
            return trimmed;
        }

        var title = Text("title", input.Title, required: true);
        var artist = Text("artist", input.Artist, required: true);
        var album = Text("album", input.Album, required: false);
        var note = Text("note", input.Note, required: false, NoteMaxLength);
        Uri? source = null;
        if (!string.IsNullOrWhiteSpace(input.SourceUrl))
        {
            try
            {
                source = WishLinkResolver.Parse(input.SourceUrl);
            }
            catch (ArcanistException)
            {
                errors.Add("sourceUrl", ErrorCodes.InvalidFormat);
            }
        }

        Guid? acoustId = null;
        if (!string.IsNullOrWhiteSpace(input.AcoustId))
        {
            if (Guid.TryParse(input.AcoustId.Trim(), out var parsed))
                acoustId = parsed;
            else
                errors.Add("acoustId", ErrorCodes.InvalidFormat);
        }

        if (input.DurationMs is <= 0 or > 24 * 3600 * 1000)
            errors.Add("durationMs", ErrorCodes.InvalidFormat);
        errors.ThrowIfAny();

        var changed = wish.Title != title || wish.Artist != artist || wish.Album != (album.Length > 0 ? album : null)
                      || wish.RecordingMbid != input.MusicBrainzRecordingId || wish.AcoustId != acoustId || wish.DurationMs != input.DurationMs;
        wish.Title = title;
        wish.Artist = artist;
        wish.Album = album.Length > 0 ? album : null;
        wish.RecordingMbid = input.MusicBrainzRecordingId;
        wish.AcoustId = acoustId;
        wish.DurationMs = input.DurationMs;
        wish.Note = note.Length > 0 ? note : null;
        wish.SourceUrl = source?.ToString();
        wish.SourceService = source is null ? null : WishLinkResolver.Identify(source);
        if (input.WantsVideo is { } wantsVideo)
        {
            if (!wantsVideo)
            {
                wish.VideoObtainedAt = null;
                wish.VideoSeen = false;
            }
            wish.WantsVideo = wantsVideo;
        }
        // Another song now: what fulfilled the old one does not count.
        if (changed && wish.ObtainedAt is not null)
        {
            wish.ObtainedAt = null;
            wish.TrackId = null;
            wish.MatchedBy = null;
            wish.FoundWhenAdded = false;
            wish.Seen = false;
            wish.VideoObtainedAt = null;
            wish.VideoSeen = false;
        }
    }

    /// <summary>A library the user can upload to, or none (the server's rule).</summary>
    private async Task<Guid?> TargetAsync(UserAccess access, Guid? libraryId, CancellationToken cancellationToken) =>
        libraryId is not { } id ? null
        : await destinations.CanTargetAsync(access, id, cancellationToken) ? id
        : throw ArcanistException.Field("targetLibraryId", ErrorCodes.NotFound);

    /// <summary>A cover the wish may use: one in the artwork cache, else none.</summary>
    private async Task<Guid?> ArtworkAsync(Guid? id, CancellationToken cancellationToken) =>
        id is { } artwork && await db.Artworks.AnyAsync(a => a.Id == artwork, cancellationToken) ? artwork : null;

    private async Task<List<Wish>> ToModelsAsync(UserAccess access, IReadOnlyList<WishListItem> rows, bool withOwners, CancellationToken cancellationToken)
    {
        var tracks = await catalog.VisibleTracksAsync(access, rows.Where(w => w.TrackId is not null).Select(w => w.TrackId!.Value).Distinct().ToList(),
            cancellationToken);
        var userIds = withOwners ? rows.Select(w => w.UserId).Distinct().ToList() : [];
        var owners = userIds.Count == 0
            ? new Dictionary<Guid, WishOwner>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
                .Select(u => new WishOwner(u.Id, u.UserName, u.DisplayName)).ToDictionaryAsync(u => u.Id, cancellationToken);
        return rows.Select(w => new Wish(w.Id, w.Title, w.Artist, w.Album, w.RecordingMbid, w.AcoustId?.ToString(), w.DurationMs, w.Note,
                w.ObtainedAt is null ? WishStatus.Wanted : WishStatus.Obtained, w.CreatedAt, w.ObtainedAt,
                w.TrackId is { } track ? tracks.GetValueOrDefault(track) : null,
                w.MatchedBy switch
                {
                    WishMatch.MusicBrainz => TrackMatchKind.MusicBrainz,
                    WishMatch.AcoustId => TrackMatchKind.AcoustId,
                    WishMatch.Tags => TrackMatchKind.Tags,
                    _ => null,
                },
                w.FoundWhenAdded, w.Seen, w.SourceUrl, w.SourceService, w.ArtworkId, owners.GetValueOrDefault(w.UserId), w.TargetLibraryId,
                w.WantsVideo, w.VideoObtainedAt, w.VideoSeen))
            .ToList();
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
