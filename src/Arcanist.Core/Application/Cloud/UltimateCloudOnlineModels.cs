namespace Arcanist.Core.Application.Cloud;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>Which of Ultimate Cloud's online services this server takes part in.</summary>
/// <param name="PersonalPlays">The plays of the administrator who linked the account count as theirs (their statistics and the rankings).</param>
/// <param name="AlbumPlaythroughs">Albums heard from start to finish feed the album rankings.</param>
/// <param name="StarTracks">The stars of the administrator who linked the account are their StarTracks, both ways.</param>
/// <param name="Metrics">The account's listening statistics are shown here.</param>
public sealed record UltimateCloudServices(bool PersonalPlays, bool AlbumPlaythroughs, bool StarTracks, bool Metrics);

public sealed record UltimateCloudServicesInput(bool PersonalPlays, bool AlbumPlaythroughs, bool StarTracks, bool Metrics);

/// <summary>An Ultimate Cloud account linked to this server, as a destination for uploads.</summary>
/// <param name="Id">The account's id in Ultimate Cloud.</param>
/// <param name="Name">Its user name or email.</param>
/// <param name="OwnerFirstName">Its owner's first name, which names its library ("Ana's Ultimate Cloud").</param>
/// <param name="UserId">The user whose own account it is (the caller); null for the server's.</param>
public sealed record LinkedCloudAccount(string Id, string? Name, string? OwnerFirstName, Guid? UserId = null);

public sealed record CloudSongRanking(string AcoustId, string? Title, string? Artist, long PlayCount);

public sealed record CloudArtistRanking(string Artist, long PlayCount);

public sealed record CloudAlbumRanking(string Title, string Artist, long PlayCount);

public sealed record CloudGenreRanking(string Genre, long PlayCount);

public sealed record CloudDecadeRanking(int DecadeStart, int DecadeEnd, long PlayCount);

/// <summary>
/// The linked account's listening as Ultimate Cloud counts it, from every Ultimate app and this server: plays, and the
/// top song, artist, album and genre of the month, the year and all time.
/// </summary>
public sealed record UltimateCloudMetrics(
    long TotalPlays,
    long TotalStreams,
    CloudSongRanking? MostStreamedSong,
    CloudSongRanking? TopSongThisMonth,
    CloudSongRanking? TopSongThisYear,
    CloudSongRanking? TopSongAllTime,
    CloudArtistRanking? TopArtistThisMonth,
    CloudArtistRanking? TopArtistThisYear,
    CloudArtistRanking? TopArtistAllTime,
    CloudAlbumRanking? TopAlbumThisMonth,
    CloudAlbumRanking? TopAlbumThisYear,
    CloudAlbumRanking? TopAlbumAllTime,
    CloudGenreRanking? TopGenreThisMonth,
    CloudGenreRanking? TopGenreThisYear,
    CloudGenreRanking? TopGenreAllTime,
    CloudDecadeRanking? TopDecade);
