using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Playlists;

namespace Arcanist.Core.Application.Discovery;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>What a home section shows; clients title it in their own language.</summary>
public enum HomeSectionKind
{
    /// <summary>Albums the user played lately.</summary>
    RecentlyPlayed,

    /// <summary>Mixes made for the user.</summary>
    Mixes,

    /// <summary>Albums that arrived lately.</summary>
    RecentlyAdded,

    /// <summary>The user's most played albums of the last three months.</summary>
    MostPlayed,

    /// <summary>The user's starred tracks, latest first.</summary>
    Starred,

    /// <summary>Albums the user played often, but not for two months.</summary>
    Rediscover,

    /// <summary>Albums the user never played.</summary>
    NeverPlayed,

    /// <summary>The user's playlists and public ones, recently changed first.</summary>
    Playlists,
}

/// <summary>A row of the home page. Only the list matching <see cref="Kind"/> has items.</summary>
public sealed record HomeSection(
    HomeSectionKind Kind,
    IReadOnlyList<Album> Albums,
    IReadOnlyList<Mix> Mixes,
    IReadOnlyList<Track> Tracks,
    IReadOnlyList<Playlist> Playlists);

/// <summary>The user's home page, section by section; empty sections are left out.</summary>
public sealed record Home(IReadOnlyList<HomeSection> Sections);

public enum MixKind
{
    /// <summary>An artist and similar ones.</summary>
    Artist,

    /// <summary>A genre.</summary>
    Genre,

    /// <summary>Albums of a decade.</summary>
    Decade,

    /// <summary>The user's best rated tracks.</summary>
    Favorites,

    /// <summary>Tracks the user never played, in the genres they like.</summary>
    Discovery,

    /// <summary>A "radio" started from a track: its artist, similar artists and its genres.</summary>
    Track,
}

/// <summary>
/// A playlist made by the server. It stays the same for the whole day and changes the next; tracks are ordered to
/// flow, by tempo and harmonically compatible keys when they have been analyzed.
/// </summary>
/// <param name="Id">Pass to <c>mix</c> to get its tracks, e.g. <c>artist:…</c>, <c>genre:…</c>, <c>decade:1990</c>,
/// <c>favorites</c>, <c>discovery</c> or <c>track:…</c>.</param>
/// <param name="Subject">What it is about (an artist's or genre's name, or the decade's first year); null for
/// favorites and discovery. Clients build the title in their own language from the kind and this.</param>
/// <param name="ArtworkIds">Up to four album covers, for a collage.</param>
public sealed record Mix(string Id, MixKind Kind, string? Subject, Guid? SubjectId, IReadOnlyList<Guid> ArtworkIds);

public sealed record MixTracks(Mix Mix, IReadOnlyList<Track> Tracks);

/// <summary>Everything an artist page shows.</summary>
/// <param name="TopTracks">The user's most played tracks of the artist, then the server's, then album order.</param>
/// <param name="Albums">Albums by the artist, newest first.</param>
/// <param name="AppearsOn">Albums by others where the artist performs (compilations, collaborations).</param>
/// <param name="RelatedArtists">Artists sharing genres or albums with this one.</param>
/// <param name="PlayCount">How many times the user played the artist's tracks.</param>
public sealed record ArtistOverview(
    Artist Artist,
    IReadOnlyList<Track> TopTracks,
    IReadOnlyList<Album> Albums,
    IReadOnlyList<Album> AppearsOn,
    IReadOnlyList<Artist> RelatedArtists,
    Mix Mix,
    int PlayCount,
    Guid? ArtworkId);
