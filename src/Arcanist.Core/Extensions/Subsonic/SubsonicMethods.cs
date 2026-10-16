using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Scanning;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Extensions.Subsonic;

/// <summary>The Subsonic methods Arcanist answers, by name (matched without case).</summary>
internal static partial class SubsonicMethods
{
    /// <summary>Methods answered without credentials.</summary>
    public static readonly IReadOnlySet<string> Public = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "getOpenSubsonicExtensions" };

    public static readonly IReadOnlyDictionary<string, Func<SubsonicCall, Task<IResult>>> All =
        new Dictionary<string, Func<SubsonicCall, Task<IResult>>>(StringComparer.OrdinalIgnoreCase)
        {
            // System
            ["ping"] = call => Task.FromResult(call.Ok()),
            ["getLicense"] = call => Task.FromResult(call.Ok(new SubsonicNode("license").Attr("valid", true))),
            ["getOpenSubsonicExtensions"] = call => Task.FromResult(call.Ok(Extensions())),
            ["tokenInfo"] = call => Task.FromResult(call.Ok(new SubsonicNode("tokenInfo").Attr("username", call.Access.UserName))),

            // Browsing
            ["getMusicFolders"] = GetMusicFoldersAsync,
            ["getIndexes"] = GetIndexesAsync,
            ["getMusicDirectory"] = GetMusicDirectoryAsync,
            ["getGenres"] = GetGenresAsync,
            ["getArtists"] = GetArtistsAsync,
            ["getArtist"] = GetArtistAsync,
            ["getAlbum"] = GetAlbumAsync,
            ["getSong"] = GetSongAsync,
            ["getArtistInfo"] = call => GetArtistInfoAsync(call, "artistInfo"),
            ["getArtistInfo2"] = call => GetArtistInfoAsync(call, "artistInfo2"),
            ["getAlbumInfo"] = GetAlbumInfoAsync,
            ["getAlbumInfo2"] = GetAlbumInfoAsync,
            ["getSimilarSongs"] = call => GetSimilarSongsAsync(call, "similarSongs"),
            ["getSimilarSongs2"] = call => GetSimilarSongsAsync(call, "similarSongs2"),
            ["getTopSongs"] = GetTopSongsAsync,

            // Lists
            ["getAlbumList"] = call => GetAlbumListAsync(call, legacy: true),
            ["getAlbumList2"] = call => GetAlbumListAsync(call, legacy: false),
            ["getRandomSongs"] = GetRandomSongsAsync,
            ["getSongsByGenre"] = GetSongsByGenreAsync,
            ["getNowPlaying"] = GetNowPlayingAsync,
            ["getStarred"] = call => GetStarredAsync(call, "starred"),
            ["getStarred2"] = call => GetStarredAsync(call, "starred2"),
            ["search2"] = call => SearchAsync(call, legacy: true),
            ["search3"] = call => SearchAsync(call, legacy: false),

            // Playlists
            ["getPlaylists"] = GetPlaylistsAsync,
            ["getPlaylist"] = GetPlaylistAsync,
            ["createPlaylist"] = CreatePlaylistAsync,
            ["updatePlaylist"] = UpdatePlaylistAsync,
            ["deletePlaylist"] = DeletePlaylistAsync,

            // Media
            ["stream"] = call => StreamAsync(call, download: false),
            ["download"] = call => StreamAsync(call, download: true),
            ["getCoverArt"] = GetCoverArtAsync,
            ["getLyrics"] = GetLyricsAsync,
            ["getLyricsBySongId"] = GetLyricsBySongIdAsync,
            ["getAvatar"] = _ => throw SubsonicException.NotFound(),

            // Annotation
            ["star"] = call => StarAsync(call, starred: true),
            ["unstar"] = call => StarAsync(call, starred: false),
            ["setRating"] = SetRatingAsync,
            ["scrobble"] = ScrobbleAsync,

            // Users and scans
            ["getUser"] = GetUserAsync,
            ["getUsers"] = GetUsersAsync,
            ["getScanStatus"] = call => ScanStatusAsync(call),
            ["startScan"] = StartScanAsync,

            // Not in Arcanist: empty answers, as apps ask for them on their home screens.
            ["getPodcasts"] = call => Empty(call, "podcasts", "channel"),
            ["getNewestPodcasts"] = call => Empty(call, "newestPodcasts", "episode"),
            ["getInternetRadioStations"] = call => Empty(call, "internetRadioStations", "internetRadioStation"),
            ["getShares"] = call => Empty(call, "shares", "share"),
            ["getVideos"] = call => Empty(call, "videos", "video"),
            ["getBookmarks"] = call => Empty(call, "bookmarks", "bookmark"),
            ["getChatMessages"] = call => Empty(call, "chatMessages", "chatMessage"),
            ["getPlayQueue"] = call => Task.FromResult(call.Ok()),
            ["savePlayQueue"] = call => Task.FromResult(call.Ok()),
        };

    /// <summary>The OpenSubsonic extensions supported, with their versions.</summary>
    private static SubsonicNode Extensions() =>
        SubsonicNode.Fragment().List("openSubsonicExtensions",
            new (string Name, int Version)[]
            {
                ("apiKeyAuthentication", 1),
                ("formPost", 1),
                ("songLyrics", 1),
                ("transcodeOffset", 1),
                ("topSongsByArtistId", 1),
            }.Select(e => new SubsonicNode("openSubsonicExtensions").Attr("name", e.Name).Values("versions", [e.Version])));

    private static Task<IResult> Empty(SubsonicCall call, string container, string item) =>
        Task.FromResult(call.Ok(new SubsonicNode(container).List(item, [])));

    private static async Task<IResult> GetMusicFoldersAsync(SubsonicCall call)
    {
        var db = call.Service<ArcanistDbContext>();
        var libraries = await LibraryService.Visible(db, call.Access).AsNoTracking().OrderBy(l => l.Number)
            .Select(l => new { l.Number, l.Name }).ToListAsync(call.Cancellation);
        return call.Ok(new SubsonicNode("musicFolders")
            .List("musicFolder", libraries.Select(l => new SubsonicNode("musicFolder").Attr("id", l.Number).Attr("name", l.Name))));
    }

    private static async Task<IResult> GetUserAsync(SubsonicCall call)
    {
        var name = call.Parameters.Required("username");
        var normalized = TextNormalizer.Normalize(name);
        if (normalized != TextNormalizer.Normalize(call.Access.UserName) && !call.Access.IsAdmin)
            throw new SubsonicException(SubsonicErrors.NotAuthorized, "Only administrators can see other users");

        var db = call.Service<ArcanistDbContext>();
        var id = await db.Users.Where(u => u.NormalizedUserName == normalized).Select(u => (Guid?)u.Id).SingleOrDefaultAsync(call.Cancellation)
                 ?? throw SubsonicException.NotFound();
        return call.Ok((await UsersAsync(call, [id]))[0]);
    }

    private static async Task<IResult> GetUsersAsync(SubsonicCall call)
    {
        if (!call.Access.IsAdmin)
            throw new SubsonicException(SubsonicErrors.NotAuthorized, "Only administrators can list users");
        var ids = await call.Service<ArcanistDbContext>().Users.OrderBy(u => u.NormalizedUserName).Select(u => u.Id).ToListAsync(call.Cancellation);
        return call.Ok(new SubsonicNode("users").List("user", await UsersAsync(call, ids)));
    }

    /// <summary>Users with their Subsonic roles, derived from their Arcanist permissions.</summary>
    private static async Task<List<SubsonicNode>> UsersAsync(SubsonicCall call, IReadOnlyList<Guid> ids)
    {
        var db = call.Service<ArcanistDbContext>();
        var cache = call.Service<UserAccessCache>();
        var numbers = await db.Libraries.ToDictionaryAsync(l => l.Id, l => l.Number, call.Cancellation);
        var users = new List<SubsonicNode>();
        foreach (var id in ids)
        {
            if (await cache.GetUserAsync(id, call.Cancellation) is not { } access)
                continue;

            var folders = access.IsAdmin ? numbers.Values : access.LibraryIds.Where(numbers.ContainsKey).Select(l => numbers[l]);
            users.Add(new SubsonicNode("user")
                .Attr("username", access.UserName)
                .Attr("scrobblingEnabled", true)
                .Attr("adminRole", access.IsAdmin)
                .Attr("settingsRole", access.Has(Permissions.ManageSettings))
                .Attr("downloadRole", access.Has(Permissions.Download))
                .Attr("uploadRole", false)
                .Attr("playlistRole", true)
                .Attr("coverArtRole", false)
                .Attr("commentRole", false)
                .Attr("podcastRole", false)
                .Attr("streamRole", true)
                .Attr("jukeboxRole", false)
                .Attr("shareRole", false)
                .Attr("videoConversionRole", false)
                .Values("folder", folders.Order().Cast<object>()));
        }

        return users;
    }

    /// <summary>Whether a library the user can see is being scanned, and how many files the scan has seen.</summary>
    private static async Task<IResult> ScanStatusAsync(SubsonicCall call, bool started = false)
    {
        var db = call.Service<ArcanistDbContext>();
        var libraries = LibraryService.Visible(db, call.Access).Select(l => l.Id);
        var jobs = db.ScanJobs.AsNoTracking().Where(j => libraries.Contains(j.LibraryId));
        var active = await jobs.Where(j => j.Status == ScanStatus.Running || j.Status == ScanStatus.Queued)
            .Select(j => j.FilesSeen).ToListAsync(call.Cancellation);
        var count = active.Count > 0
            ? active.Sum()
            : await jobs.OrderByDescending(j => j.RequestedAt).Select(j => j.FilesSeen).FirstOrDefaultAsync(call.Cancellation);
        return call.Ok(new SubsonicNode("scanStatus").Attr("scanning", started || active.Count > 0).Attr("count", count));
    }

    /// <summary>Scans every library the user can see (<c>fullScan=true</c> rereads every file). Needs RUN_SCANS.</summary>
    private static async Task<IResult> StartScanAsync(SubsonicCall call)
    {
        call.Service<Application.AccessGuard>().Permission(Permissions.RunScans);
        var scans = call.Service<Application.Scanning.ScanService>();
        var full = call.Parameters.Bool("fullScan") ?? false;
        var db = call.Service<ArcanistDbContext>();
        var libraries = await LibraryService.Visible(db, call.Access).Select(l => l.Id).ToListAsync(call.Cancellation);
        foreach (var library in libraries)
            await scans.StartAsync(library, full, call.Cancellation);
        // Just started: a small library can finish before the status is read, and apps expect "scanning" here.
        return await ScanStatusAsync(call, started: libraries.Count > 0);
    }
}
