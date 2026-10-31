using System.Reflection;
using Arcanist.Core.Capabilities;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Application.Server;

/// <summary>Bound from <c>Arcanist</c>.</summary>
public sealed class ServerOptions
{
    /// <summary>
    /// Where users of this server can obtain its source code, as required by section 13 of the AGPL. Anyone
    /// running a modified version must point this to their modified source. Defaults to the repository the build
    /// came from (<c>RepositoryUrl</c> in <c>Directory.Build.props</c>).
    /// </summary>
    public string? SourceUrl { get; set; } = DefaultSourceUrl;

    internal static readonly string? DefaultSourceUrl = typeof(ServerOptions).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "RepositoryUrl")?.Value;
}

/// <summary>Public description of the server, for clients deciding how to talk to it. Part of the API contract.</summary>
/// <param name="ApiVersion">Major version of the GraphQL and REST contracts. Incremented only on breaking changes.</param>
/// <param name="Capabilities">Ultimate Cloud features currently enabled; local features are always available.</param>
/// <param name="TranscodingFormats">Formats the stream endpoint can convert to (none without ffmpeg).</param>
/// <param name="RatingMode">How users rate tracks here: show five stars, or a single star.</param>
/// <param name="Features">Parts of the API this version has, for clients that also talk to older servers.</param>
/// <param name="Edition">What the server is: open source only, with closed source parts (Standard), or with Ultimate Cloud unlocked.</param>
/// <param name="OpenSourceOnly">The 100% open source mode is on: closed source components neither install nor run.</param>
public sealed record ServerInfo(
    string Name,
    string Version,
    int ApiVersion,
    string? SourceUrl,
    bool SetupCompleted,
    IReadOnlyList<Capability> Capabilities,
    CloudTier CloudTier,
    IReadOnlyList<Core.Media.TranscodeFormat> TranscodingFormats,
    Data.Entities.RatingMode RatingMode,
    IReadOnlyList<ApiFeature> Features,
    Ai.TrackAnalysisProgress? TrackAnalysis,
    ServerEdition Edition = ServerEdition.OpenSource,
    bool OpenSourceOnly = false);

/// <summary>A part of the API that clients may need to know a server has (older versions lack it).</summary>
public enum ApiFeature
{
    /// <summary><c>libraryId</c> on tracks, albums and artists, and <c>Track.libraryIds</c>.</summary>
    LibraryFilter,

    /// <summary>Uploading files to a library (<c>POST /rest/v1/libraries/{id}/uploads</c>, permission UPLOAD).</summary>
    Uploads,

    /// <summary><c>matchTracks</c>: finding the server's tracks for an app's own.</summary>
    TrackMatching,

    /// <summary><c>importListening</c>: bringing an app's ratings and play counts.</summary>
    ListeningImport,

    /// <summary>
    /// The user's StarTracks: <c>starTracks</c>, <c>addStarTracks</c>/<c>removeStarTracks</c> (also by AcoustID) and the
    /// <c>starTracksChanged</c> subscription.
    /// </summary>
    StarTracks,

    /// <summary>The user's history of uploads: <c>myUploads</c> and <c>clearMyUploads</c>.</summary>
    UploadHistory,

    /// <summary><c>uploadTracksToMyCloud</c>: sending songs to the user's own Ultimate Cloud library.</summary>
    MyCloudUploads,

    /// <summary>
    /// Songs' analyses for DJ software (the AI extension, while it is on): <c>trackAnalysis</c>, <c>trackAnalyses</c>,
    /// <c>prioritizeTrackAnalysis</c>, <c>trackAnalysisReady</c> and the audio windows; <c>trackAnalysis</c> in serverInfo
    /// has the analyzer's version and the library's progress.
    /// </summary>
    TrackAnalysis,

    /// <summary>Listening statistics per song: <c>trackStats</c> (the user's, or the server's).</summary>
    TrackStats,

    /// <summary>
    /// <c>extensionApps</c>: pages of the extensions that players open in a window or a tab, with the host protocol;
    /// <c>MyUpload.source</c> names the extension a file came from.
    /// </summary>
    ExtensionApps,

    /// <summary>
    /// <c>libraries(includeVirtual: true)</c> lists "All" (<c>Library.virtual</c>), every library the user can access;
    /// its id works as any <c>libraryId</c> filter and nothing can be uploaded to it.
    /// </summary>
    VirtualLibraries,

    /// <summary>
    /// Each user's wish list: <c>wishList</c>, <c>addWish</c>, <c>updateWish</c>, <c>removeWish</c>,
    /// <c>markWishesSeen</c> and the <c>wishObtained</c> subscription; <c>albums(sort:)</c> too.
    /// </summary>
    WishList,

    /// <summary>
    /// <c>streamingDefaults</c>: the quality each kind of Ultimate client plays by default, and <c>client</c> on the
    /// stream to apply it.
    /// </summary>
    StreamingDefaults,

    /// <summary>
    /// <c>client</c> on uploads (PANEL, DESKTOP, WEB, MOBILE), shown in <c>uploads</c>: the log of every upload that
    /// administrators see in the panel's Import page.
    /// </summary>
    UploadLog,

    /// <summary>
    /// <c>uploadSettings</c> (the library uploads go to when they name none), <c>POST /rest/v1/uploads</c> (an upload
    /// without a library, optionally for a wish) and <c>targetLibraryId</c> on wishes.
    /// </summary>
    UploadDestination,

    /// <summary>
    /// Editing tracks' tags, written to their files: <c>editTrack</c> (<c>PATCH /rest/v1/tracks/{id}</c>) and
    /// <c>editTracks</c>; permission EDIT_METADATA; <c>Track.canEdit</c>.
    /// </summary>
    TrackEditing,

    /// <summary>
    /// Deleting tracks (their files go to the trash): <c>deleteTrack</c> (<c>DELETE /rest/v1/tracks/{id}</c>) and
    /// <c>deleteTracks</c>; permission DELETE_TRACKS; <c>Track.canDelete</c>.
    /// </summary>
    TrackDeletion,

    /// <summary>
    /// Replacing and removing covers, embedded in the files: <c>PUT|DELETE /rest/v1/tracks/{id}/artwork</c> and
    /// <c>/rest/v1/albums/{id}/artwork</c> (permission EDIT_METADATA).
    /// </summary>
    ArtworkEditing,

    /// <summary>
    /// What a properties dialog shows: <c>TrackFile</c> has its library, path, hash and dates, and <c>Track</c> its album
    /// artist, release, comment, tagged tempo and <c>updatedAt</c>.
    /// </summary>
    TrackProperties,

    /// <summary><c>myStorage</c>: the music of each library the user can access, and the free space of its disk.</summary>
    StorageUsage,

    /// <summary>
    /// Choosing where uploads go: <c>libraryId</c> on <c>POST /rest/v1/uploads</c>, and each user's own default
    /// (<c>myUploadSettings</c>/<c>setMyUploadSettings</c>), for apps that use the whole server as one library.
    /// </summary>
    UploadTarget,

    /// <summary>
    /// Incremental refresh: <c>changedSince</c> on <c>tracks</c> and <c>playlists</c>, <c>TrackPage.serverTime</c>, and
    /// <c>trackIds</c> to see which tracks left.
    /// </summary>
    ChangesSince,

    /// <summary><c>renameLibrary</c> (<c>PUT /rest/v1/libraries/{id}/name</c>): only the name, for managers and owners.</summary>
    LibraryRename,

    /// <summary>
    /// Music videos: <c>Track.videos</c>, <c>musicVideos</c>, their streams with the song's audio or their own
    /// (<c>/rest/v1/videos/{id}/stream</c>, HLS when prepared), uploads (<c>POST /rest/v1/tracks/{id}/video</c>) and
    /// <c>myVideoSettings</c>.
    /// </summary>
    MusicVideos,

    /// <summary>
    /// <c>musicVideoLyrics</c> (<c>GET /rest/v1/videos/{id}/lyrics</c>): a music video's lyrics on its own timeline,
    /// following where the video plays its song and leaving its other scenes without lyrics.
    /// </summary>
    VideoLyrics,

    /// <summary>
    /// Recognizing the words of songs without lyrics with a model (the Lyrics Sync extension, while it runs):
    /// <c>lyricsTranscription</c>, <c>requestLyricsTranscription</c>, and the review of the draft it writes
    /// (<c>saveLyricsDraft</c>, <c>acceptLyricsDraft</c>, <c>discardLyricsDraft</c>, <c>lyricsDrafts</c>).
    /// </summary>
    LyricsTranscription,

    /// <summary>
    /// Playlists shared with users by role (<c>setPlaylistMembers</c>, <c>Playlist.members</c>, the MANAGER role), public
    /// links with 30-second previews (<c>createPlaylistLink</c>, <c>sharedPlaylist</c>, <c>sharing</c>) and the
    /// <c>playlistChanged</c> subscription.
    /// </summary>
    PlaylistSharing,

    /// <summary>Jukebox Jams (<c>startJukeboxJam</c>, <c>jukeboxJam</c>…), their host API and their guests.</summary>
    JukeboxJam,

    /// <summary>A Jam's host can say Ultimate's AI DJ chooses what plays (<c>setJukeboxJamDj</c>, <c>JukeboxJam.dj</c>).</summary>
    JukeboxJamAiDj,

    /// <summary><c>availablePlayers</c>: the web players any user can open.</summary>
    AvailablePlayers,
}

public sealed class ServerInfoService(
    ICapabilityService capabilities,
    ICloudLinkState cloudLink,
    IOptionsMonitor<ServerOptions> options,
    IOptionsMonitor<Setup.SetupOptions> setup,
    Core.Media.FfmpegLocator ffmpeg,
    Core.Media.Transcoder transcoder,
    Listening.RatingSettings ratings,
    Core.Extensions.ExtensionHost extensions,
    Core.Ai.AiMonitor ai,
    EditionResolver edition)
{
    public const int ApiVersion = 1;

    private static readonly string Version = Hosting.ArcanistVersion.Current;

    /// <summary>Available without authentication.</summary>
    public ServerInfo Get() => new(
        "Arcanist",
        Version,
        ApiVersion,
        options.CurrentValue.SourceUrl,
        setup.CurrentValue.Completed,
        Enum.GetValues<Capability>().Where(capabilities.IsEnabled).ToList(),
        cloudLink.Tier,
        transcoder.Formats(ffmpeg.Current),
        ratings.Current,
        Enum.GetValues<ApiFeature>().Where(f => (f != ApiFeature.TrackAnalysis || AnalysisOn)
            && (f != ApiFeature.LyricsTranscription || Application.Catalog.LyricsTimingService.StateOf(extensions) == Application.Catalog.LyricsTimingExtension.Running)).ToList(),
        AnalysisOn ? ai.Current.Progress with { AnalysisVersion = Core.Analysis.DjAudioAnalyzer.Version } : null,
        edition.Current,
        extensions.OpenSourceOnly);

    private bool AnalysisOn => extensions.IsEnabled(Core.Extensions.Ai.AiExtension.Id) && Core.Analysis.DjAudioAnalyzer.IsAvailable;
}
