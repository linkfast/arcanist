namespace Arcanist.Core.Application.Cloud;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>Which of Ultimate Cloud's online services this server takes part in.</summary>
/// <param name="PersonalPlays">The plays of the administrator who linked the account count as theirs (their statistics and the rankings).</param>
/// <param name="SharedPlays">Everyone else's plays feed the global rankings anonymously, never anyone's statistics.</param>
/// <param name="AlbumPlaythroughs">Albums heard from start to finish feed the album rankings.</param>
/// <param name="StarTracks">The stars of the administrator who linked the account are their StarTracks, both ways.</param>
/// <param name="Metrics">The account's listening statistics are shown here.</param>
public sealed record UltimateCloudServices(bool PersonalPlays, bool SharedPlays, bool AlbumPlaythroughs, bool StarTracks, bool Metrics);

public sealed record UltimateCloudServicesInput(bool PersonalPlays, bool SharedPlays, bool AlbumPlaythroughs, bool StarTracks, bool Metrics);

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
