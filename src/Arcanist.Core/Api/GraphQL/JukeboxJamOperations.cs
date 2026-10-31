using System.ComponentModel;
using Arcanist.Core.Application.Jams;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("myJukeboxJams")]
    [Description("The Jukebox Jams the user hosts or takes part in that have not ended.")]
    public static Task<IReadOnlyList<JukeboxJamSummary>> MyJukeboxJams([FromServices] JukeboxJamService jams, CancellationToken cancellationToken) =>
        jams.MineAsync(cancellationToken);

    [Name("jukeboxJam")]
    [Description("A Jukebox Jam by its code, as the one asking sees it: what plays, the queue in the order it will play, the DJ if there is one. Users of the server with its code; without an account, only Jams open to guests (with `guestToken` once joined). An ended one says so (`endedAt`).")]
    public static Task<JukeboxJam> JukeboxJam(string code, string? guestToken, [FromServices] JukeboxJamService jams, CancellationToken cancellationToken) =>
        jams.GetAsync(code, guestToken, cancellationToken);

    [Name("jukeboxJamSearch")]
    [Description("Songs that can be added to a Jam (from its libraries, among those the host can listen to). For those taking part.")]
    public static Task<IReadOnlyList<JukeboxJamSearchResult>> JukeboxJamSearch(string code, string query, int? first, string? guestToken,
        [FromServices] JukeboxJamService jams, CancellationToken cancellationToken) =>
        jams.SearchAsync(code, query, first, guestToken, cancellationToken);

    [Name("jukeboxJamArtists")]
    [Description("The artists (album artists) of the songs that can be added to a Jam, to browse them; `search` filters by name, `after` is the previous page's nextCursor. For those taking part.")]
    public static Task<JukeboxJamArtistPage> JukeboxJamArtists(string code, string? search, string? after, int? first, string? guestToken,
        [FromServices] JukeboxJamService jams, CancellationToken cancellationToken) =>
        jams.ArtistsAsync(code, search, after, first, guestToken, cancellationToken);

    [Name("jukeboxJamAlbums")]
    [Description("Albums with songs that can be added to a Jam: an artist's (by year) or all of them (by title, or RECENTLY_ADDED); `search` filters by title or artist. For those taking part.")]
    public static Task<JukeboxJamAlbumPage> JukeboxJamAlbums(string code, [Id] Guid? artistId, string? search, JukeboxJamAlbumSort? sort, string? after,
        int? first, string? guestToken, [FromServices] JukeboxJamService jams, CancellationToken cancellationToken) =>
        jams.AlbumsAsync(code, artistId, search, sort, after, first, guestToken, cancellationToken);

    [Name("jukeboxJamAlbumSongs")]
    [Description("An album's songs that can be added to a Jam, in disc and track order. For those taking part.")]
    public static Task<IReadOnlyList<JukeboxJamSearchResult>> JukeboxJamAlbumSongs(string code, [Id] Guid albumId, string? guestToken,
        [FromServices] JukeboxJamService jams, CancellationToken cancellationToken) =>
        jams.AlbumSongsAsync(code, albumId, guestToken, cancellationToken);

    [Name("jukeboxJamNext")]
    [Description("Host API: the song the host's client should play next (pinned, then most voted, then by arrival), with its stream; null when nothing waits. Host only.")]
    public static Task<JukeboxJamNext?> JukeboxJamNext(string code, [FromServices] JukeboxJamService jams, CancellationToken cancellationToken) =>
        jams.NextAsync(code, cancellationToken);
}

public sealed partial class Mutation
{
    [Name("startJukeboxJam")]
    [Description("Starts a Jukebox Jam the user hosts: a live queue (a playlist of its own) that plays on a client of theirs (any that implements the host API), the server's audio or a DLNA renderer. Opening it to guests without an account needs SHARE_LINKS and the server's setting.")]
    public static Task<JukeboxJam> StartJukeboxJam(StartJukeboxJamInput input, [FromServices] JukeboxJamService jams, CancellationToken cancellationToken) =>
        jams.StartAsync(input, cancellationToken);

    [Name("joinJukeboxJam")]
    [Description("Takes part in a Jam. A signed-in user becomes a member (null is returned: read jukeboxJam); someone without an account gets a guest session by the name they give (send its token as `guestToken`).")]
    public static Task<JukeboxJamGuestSession?> JoinJukeboxJam(string code, string? name, [FromServices] JukeboxJamService jams, CancellationToken cancellationToken) =>
        jams.JoinAsync(code, name, cancellationToken);

    [Name("addToJukeboxJam")]
    [Description("Adds songs to a Jam's queue (at most 50 at once). Guests within their limit (jam_guest_limit, with retryAfterSeconds); in voting Jams each comes with a vote of whoever adds it. `source: DJ` is the host's DJ: no limit and no vote.")]
    public static Task<JukeboxJam> AddToJukeboxJam(string code, [Id] IReadOnlyList<Guid> trackIds, JukeboxJamAddSource? source, string? guestToken,
        [FromServices] JukeboxJamService jams, CancellationToken cancellationToken) =>
        jams.AddAsync(code, trackIds, source, guestToken, cancellationToken);

    [Name("replaceJukeboxJamNowPlaying")]
    [Description("Plays a song now in place of the one playing (which ends). Anyone taking part when the Jam allows it (allowReplace; jam_replace_off otherwise), once every 60 seconds in the whole Jam (jam_replace_too_soon with retryAfterSeconds); the host and those who manage it always. For guests it counts as one of their songs.")]
    public static Task<JukeboxJam> ReplaceJukeboxJamNowPlaying(string code, [Id] Guid trackId, string? guestToken, [FromServices] JukeboxJamService jams,
        CancellationToken cancellationToken) =>
        jams.ReplaceAsync(code, trackId, guestToken, cancellationToken);

    [Name("voteJukeboxJam")]
    [Description("A vote for a waiting song (voting Jams). As often as one likes, but once every 30 seconds in the whole Jam: sooner is jam_vote_too_soon with retryAfterSeconds.")]
    public static Task<JukeboxJam> VoteJukeboxJam(string code, [Id] Guid entryId, string? guestToken, [FromServices] JukeboxJamService jams,
        CancellationToken cancellationToken) =>
        jams.VoteAsync(code, entryId, guestToken, cancellationToken);

    [Name("controlJukeboxJam")]
    [Description("Skips, pauses, resumes, removes, pins or moves (Jams without voting) songs. The host and those who manage its playlist.")]
    public static Task<JukeboxJam> ControlJukeboxJam(string code, ControlJukeboxJamInput input, [FromServices] JukeboxJamService jams,
        CancellationToken cancellationToken) =>
        jams.ControlAsync(code, input, cancellationToken);

    [Name("updateJukeboxJam")]
    [Description("Changes a Jam's name, voting, guests, whether anyone may replace the song playing, and libraries. Host only.")]
    public static Task<JukeboxJam> UpdateJukeboxJam(string code, UpdateJukeboxJamInput input, [FromServices] JukeboxJamService jams,
        CancellationToken cancellationToken) =>
        jams.UpdateAsync(code, input, cancellationToken);

    [Name("removeJukeboxJamGuest")]
    [Description("Takes a guest out of a Jam: their session no longer works. The host and those who manage it.")]
    public static Task<JukeboxJam> RemoveJukeboxJamGuest(string code, [Id] Guid guestId, [FromServices] JukeboxJamService jams,
        CancellationToken cancellationToken) =>
        jams.RemoveGuestAsync(code, guestId, cancellationToken);

    [Name("endJukeboxJam")]
    [Description("Ends a Jam: its code no longer works and what played stays as a playlist of the host's. Host only.")]
    public static Task<JukeboxJam> EndJukeboxJam(string code, [FromServices] JukeboxJamService jams, CancellationToken cancellationToken) =>
        jams.EndAsync(code, cancellationToken);

    [Name("claimJukeboxJamHost")]
    [Description("Host API: this client (a web player tab, Ultimate Desktop) plays the Jam; another one claiming it takes over (the one before gets a HOST change with the new device). Host only, for Jams that play on a client.")]
    public static Task<JukeboxJam> ClaimJukeboxJamHost(string code, ClaimJukeboxJamHostInput input, [FromServices] JukeboxJamService jams,
        CancellationToken cancellationToken) =>
        jams.ClaimHostAsync(code, input, cancellationToken);

    [Name("reportJukeboxJamPlayback")]
    [Description("Host API: what the host's client plays (an entry of the Jam, which leaves the queue; a song not on the server; or nothing), paused or not and where. Every 5 seconds and on every change, from the device that claimed it (jam_not_host otherwise).")]
    public static Task<JukeboxJam> ReportJukeboxJamPlayback(string code, ReportJukeboxJamPlaybackInput input, [FromServices] JukeboxJamService jams,
        CancellationToken cancellationToken) =>
        jams.ReportAsync(code, input, cancellationToken);

    [Name("setJukeboxJamDj")]
    [Description("Host API: a DJ chooses what plays (ULTIMATE_AI_DJ: Ultimate's AI DJ), or no longer (NONE); everyone in the Jam sees it. Taken off by itself when the host's client is gone for 5 minutes. Host only.")]
    public static Task<JukeboxJam> SetJukeboxJamDj(string code, SetJukeboxJamDjInput input, [FromServices] JukeboxJamService jams,
        CancellationToken cancellationToken) =>
        jams.SetDjAsync(code, input, cancellationToken);
}

public sealed partial class Subscription
{
    [Name("jukeboxJamChanged")]
    [Description("Something changed in a Jam (its queue, what plays, its DJ…): read it again with jukeboxJam. Anyone with its code, also without an account. The host's client also acts on COMMAND (skip, pause, resume) and stops playing it on a HOST change for another device.")]
    public static IObservable<JukeboxJamChange> JukeboxJamChanged(string code, [FromServices] JukeboxJamService jams) => jams.Watch(code);
}
