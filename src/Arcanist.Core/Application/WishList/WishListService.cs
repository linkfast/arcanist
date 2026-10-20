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

/// <summary>A song the user wants.</summary>
/// <param name="AcoustId">The AcoustID the user identified it by.</param>
/// <param name="Track">The song that fulfilled it, while the user can see it.</param>
/// <param name="MatchedBy">What made that track this song.</param>
/// <param name="FoundWhenAdded">A library had it already when the wish was made.</param>
/// <param name="Seen">The user saw that it arrived (<c>markWishesSeen</c>).</param>
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
    bool Seen);

/// <param name="Album">Only a copy from this album fulfils it.</param>
/// <param name="DurationMs">Only a copy this long (give or take three seconds) fulfils it.</param>
public sealed record WishInput(
    string Title,
    string Artist,
    string? Album = null,
    Guid? MusicBrainzRecordingId = null,
    string? AcoustId = null,
    int? DurationMs = null,
    string? Note = null);

/// <param name="Items">The wanted ones by the newest wish, then the obtained ones by when they arrived.</param>
/// <param name="Wanted">How many are still wanted.</param>
/// <param name="Unseen">How many arrived and the user has not seen yet.</param>
public sealed record WishListResult(IReadOnlyList<Wish> Items, int Wanted, int Unseen);

/// <summary>A wish of the user came true: the song is in a library they can play.</summary>
public sealed record WishObtained(Guid WishId, string Title, string Artist, Guid? TrackId, DateTime ObtainedAt);

/// <summary>
/// Each user's wish list: songs they want, recognised as soon as one arrives in a library they can play (after every
/// scan, so also uploads, imports and copies) by MusicBrainz recording, AcoustID, or title and artist (and album and
/// duration when given). The user is told by the <c>wishObtained</c> subscription, and the list keeps the history.
/// </summary>
public sealed class WishListService(
    ArcanistDbContext db,
    AccessGuard guard,
    CatalogService catalog,
    WishListSignals signals,
    TimeProvider time)
{
    public const int MaxWishes = 2000;
    public const int TextMaxLength = 300;
    public const int NoteMaxLength = 1000;

    public async Task<WishListResult> WishListAsync(WishStatus? status, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var mine = db.WishListItems.AsNoTracking().Where(w => w.UserId == access.UserId);
        var wanted = await mine.CountAsync(w => w.ObtainedAt == null, cancellationToken);
        var unseen = await mine.CountAsync(w => w.ObtainedAt != null && !w.Seen, cancellationToken);

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
        return new WishListResult(await ToModelsAsync(access, rows, cancellationToken), wanted, unseen);
    }

    /// <summary>Adds a wish; when a library the user can play has the song already, it comes obtained.</summary>
    public async Task<Wish> AddAsync(WishInput input, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var wish = new WishListItem { UserId = access.UserId, Title = "", Artist = "", CreatedAt = Now() };
        Apply(wish, input);
        if (await db.WishListItems.CountAsync(w => w.UserId == access.UserId, cancellationToken) >= MaxWishes)
            throw ArcanistException.Field("title", ErrorCodes.TooMany);

        db.WishListItems.Add(wish);
        if (await MatchAsync(catalog, access, [wish], Now(), cancellationToken) > 0)
        {
            wish.FoundWhenAdded = true;
            wish.Seen = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        return (await ToModelsAsync(access, [wish], cancellationToken))[0];
    }

    /// <summary>Changes a wish; a wanted one is looked for again.</summary>
    public async Task<Wish> UpdateAsync(Guid id, WishInput input, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var wish = await db.WishListItems.SingleOrDefaultAsync(w => w.Id == id && w.UserId == access.UserId, cancellationToken)
                   ?? throw new ArcanistException.NotFound();
        Apply(wish, input);
        if (wish.ObtainedAt is null && await MatchAsync(catalog, access, [wish], Now(), cancellationToken) > 0)
        {
            wish.FoundWhenAdded = true;
            wish.Seen = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        return (await ToModelsAsync(access, [wish], cancellationToken))[0];
    }

    public async Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = guard.Authenticated().UserId;
        if (await db.WishListItems.Where(w => w.Id == id && w.UserId == user).ExecuteDeleteAsync(cancellationToken) == 0)
            throw new ArcanistException.NotFound();
        return true;
    }

    /// <summary>The user saw what arrived. Returns how many were new to them.</summary>
    public async Task<int> MarkSeenAsync(CancellationToken cancellationToken)
    {
        var user = guard.Authenticated().UserId;
        return await db.WishListItems.Where(w => w.UserId == user && w.ObtainedAt != null && !w.Seen)
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
        // Another song now: what fulfilled the old one does not count.
        if (changed && wish.ObtainedAt is not null)
        {
            wish.ObtainedAt = null;
            wish.TrackId = null;
            wish.MatchedBy = null;
            wish.FoundWhenAdded = false;
            wish.Seen = false;
        }
    }

    private async Task<List<Wish>> ToModelsAsync(UserAccess access, IReadOnlyList<WishListItem> rows, CancellationToken cancellationToken)
    {
        var tracks = await catalog.VisibleTracksAsync(access, rows.Where(w => w.TrackId is not null).Select(w => w.TrackId!.Value).Distinct().ToList(),
            cancellationToken);
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
                w.FoundWhenAdded, w.Seen))
            .ToList();
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
