namespace Arcanist.Extensibility;

/// <summary>
/// The wish lists of the server's users, for extensions: each user's (songs they want, and those that arrived) and
/// the server's (everyone's). Get it from the services (<c>IWishLists</c>). It does not check permissions: an extension
/// that shows another user's wishes to someone decides who may see them (the server's own rule is VIEW_ALL_WISHES).
/// Adding a wish looks for the song at once, among the libraries that user can play, like the wish list's own page.
/// </summary>
public interface IWishLists
{
    /// <summary>One user's wishes: the wanted ones by the newest, then those that arrived.</summary>
    Task<IReadOnlyList<WishListEntry>> ListAsync(Guid userId, bool wantedOnly, CancellationToken cancellationToken);

    /// <summary>Everyone's wishes (the server's wish list), each with its user.</summary>
    Task<IReadOnlyList<WishListEntry>> ListAllAsync(bool wantedOnly, CancellationToken cancellationToken);

    /// <summary>Adds a wish for the user; it comes obtained when a library they can play has the song already.</summary>
    Task<WishListEntry> AddAsync(Guid userId, WishListEntryInput input, CancellationToken cancellationToken);

    /// <summary>Removes a wish of the user. False when they have no such wish.</summary>
    Task<bool> RemoveAsync(Guid userId, Guid wishId, CancellationToken cancellationToken);

    /// <summary>
    /// What a song's link from a streaming service says (Spotify, YouTube Music, Deezer, Apple Music, MusicBrainz…), to
    /// add it: null when external services are not allowed or the link is not a song that can be read.
    /// </summary>
    Task<WishListEntryInput?> ResolveLinkAsync(string url, CancellationToken cancellationToken);
}

/// <summary>A wish.</summary>
/// <param name="ObtainedAt">When the song arrived; null while it is wanted.</param>
/// <param name="TrackId">The track that fulfilled it, when it arrived.</param>
/// <param name="SourceUrl">The streaming service's page it was added from.</param>
/// <param name="ArtworkId">Its cover, served at <c>/rest/v1/artwork/{id}</c>.</param>
public sealed record WishListEntry(
    Guid Id,
    Guid UserId,
    string UserName,
    string Title,
    string Artist,
    string? Album,
    int? DurationMs,
    Guid? MusicBrainzRecordingId,
    string? AcoustId,
    string? Note,
    DateTime CreatedAt,
    DateTime? ObtainedAt,
    Guid? TrackId,
    string? SourceUrl,
    Guid? ArtworkId);

/// <summary>A song to wish for: title and artist; the rest narrows it.</summary>
public sealed record WishListEntryInput(
    string Title,
    string Artist,
    string? Album = null,
    int? DurationMs = null,
    Guid? MusicBrainzRecordingId = null,
    string? AcoustId = null,
    string? Note = null,
    string? SourceUrl = null,
    Guid? ArtworkId = null);
