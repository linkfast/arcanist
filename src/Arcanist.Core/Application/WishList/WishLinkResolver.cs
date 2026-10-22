using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Arcanist.Core.Application.Server;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Online;
using Arcanist.Core.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Application.WishList;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>What a link to a song says about it, to fill a wish before adding it.</summary>
/// <param name="Url">The link as given (short links are followed).</param>
/// <param name="MusicBrainzRecordingId">The recording in MusicBrainz, found by the link's ISRC, or its title and artist.</param>
/// <param name="ArtworkId">Its cover, served by the artwork endpoint: the Cover Art Archive's, or the service's own.</param>
public sealed record WishLinkPreview(
    WishLinkService Service,
    string Url,
    string Title,
    string Artist,
    string? Album,
    int? DurationMs,
    Guid? MusicBrainzRecordingId,
    Guid? ArtworkId);

public sealed record ResolveWishLinkInput(string Url);

/// <summary>
/// Reads a song's link from a streaming service (Spotify, YouTube Music, Deezer, Apple Music, MusicBrainz, Tidal,
/// SoundCloud, Bandcamp, Amazon Music… or any page that describes a song) into a wish: title, artist, album and
/// duration from the service (its public API, oEmbed or the page's metadata), the MusicBrainz recording (by ISRC, or by
/// title and artist), and a cover (the Cover Art Archive's first, the service's otherwise), kept in the artwork cache.
/// Only links the user gives are read, and only with external services allowed (Privacy).
/// </summary>
public sealed partial class WishLinkResolver(
    IHttpClientFactory clients,
    PrivacySettings privacy,
    CoverArtClient coverArt,
    ArtworkStore artworkStore,
    ArcanistDbContext db,
    WishLinkGate gate,
    ILogger<WishLinkResolver> logger)
{
    public const string HttpClientName = "wish-links";
    private const int MaxPageBytes = 3 * 1024 * 1024;
    private const int MaxImageBytes = 10 * 1024 * 1024;

    public async Task<WishLinkPreview> ResolveAsync(string url, CancellationToken cancellationToken)
    {
        if (!await privacy.OnlineServicesAllowedAsync(cancellationToken))
            throw new ArcanistException.Conflict(ErrorCodes.ExternalServicesDisabled);
        var uri = Parse(url);
        var http = clients.CreateClient(HttpClientName);

        // Short links (link.deezer.com, spotify.link, youtu.be…) say where they go.
        if (IsShortLink(uri))
            uri = await FollowAsync(http, uri, cancellationToken);

        var service = Identify(uri);
        Found? found;
        try
        {
            found = service switch
            {
                WishLinkService.Deezer => await DeezerAsync(http, uri, cancellationToken),
                WishLinkService.AppleMusic => await AppleMusicAsync(http, uri, cancellationToken),
                WishLinkService.YouTube or WishLinkService.YouTubeMusic => await YouTubeAsync(http, uri, cancellationToken),
                WishLinkService.MusicBrainz => await MusicBrainzAsync(uri, cancellationToken),
                WishLinkService.SoundCloud => await SoundCloudAsync(http, uri, cancellationToken),
                WishLinkService.Spotify => await SpotifyAsync(http, uri, cancellationToken),
                _ => await PageAsync(http, uri, cancellationToken),
            };
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException or XmlException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Could not read the link {Url}: {Error}", uri, e.Message);
            throw new ArcanistException.Conflict(ErrorCodes.WishLinkUnreadable);
        }

        if (found is not { Title.Length: > 0, Artist.Length: > 0 })
            throw new ArcanistException.Conflict(ErrorCodes.WishLinkUnreadable);

        // A MusicBrainz link or the ISRC say the exact recording: its cover is the Cover Art Archive's. A search by
        // title, artist and duration only finds a likely one: the service's own image (the art of that very link)
        // goes first then.
        var (recording, release, exact) = (found.RecordingMbid, found.ReleaseMbid, found.RecordingMbid is not null);
        if (recording is null)
            (recording, release, exact) = await FindInMusicBrainzAsync(found, cancellationToken);

        var artwork = await ArtworkAsync(http, release, found.ImageUrl, coverArtFirst: exact, cancellationToken);
        return new WishLinkPreview(service, uri.ToString(), Clip(found.Title), Clip(found.Artist), found.Album is { Length: > 0 } album ? Clip(album) : null,
            found.DurationMs is > 0 ? found.DurationMs : null, recording, artwork);
    }

    /// <summary>The service a link is from, by its host.</summary>
    public static WishLinkService Identify(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        bool Is(string domain) => host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);
        return host switch
        {
            _ when Is("music.youtube.com") => WishLinkService.YouTubeMusic,
            _ when Is("youtube.com") || Is("youtu.be") => WishLinkService.YouTube,
            _ when Is("spotify.com") || Is("spotify.link") => WishLinkService.Spotify,
            _ when Is("deezer.com") || Is("deezer.page.link") => WishLinkService.Deezer,
            _ when Is("music.apple.com") || Is("itunes.apple.com") => WishLinkService.AppleMusic,
            _ when Is("musicbrainz.org") => WishLinkService.MusicBrainz,
            _ when Is("tidal.com") => WishLinkService.Tidal,
            _ when Is("soundcloud.com") => WishLinkService.SoundCloud,
            _ when Is("bandcamp.com") => WishLinkService.Bandcamp,
            _ when host.StartsWith("music.amazon.", StringComparison.Ordinal) => WishLinkService.AmazonMusic,
            _ => WishLinkService.Other,
        };
    }

    /// <summary>An http(s) link, as wishes keep it.</summary>
    public static Uri Parse(string? url)
    {
        var text = url?.Trim() ?? "";
        // A link pasted with words around it ("Listen to X on Spotify: https://…").
        if (LinkInText().Match(text) is { Success: true } match)
            text = match.Value;
        if (text.Length is 0 or > 2000 || !Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || uri.HostNameType is not UriHostNameType.Dns)
            throw ArcanistException.Field("url", ErrorCodes.InvalidFormat);
        return uri;
    }

    internal sealed record Found(string Title, string Artist, string? Album = null, int? DurationMs = null, string? Isrc = null,
        Guid? RecordingMbid = null, Guid? ReleaseMbid = null, string? ImageUrl = null);

    private static bool IsShortLink(Uri uri) =>
        uri.Host is "link.deezer.com" or "deezer.page.link" or "spotify.link" or "spoti.fi" or "on.soundcloud.com";

    private static async Task<Uri> FollowAsync(HttpClient http, Uri uri, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return response.RequestMessage?.RequestUri ?? uri;
    }

    private static string? Segment(Uri uri, string kind) =>
        uri.Segments.Select(s => s.Trim('/')).SkipWhile(s => !s.Equals(kind, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();

    private static void RequireSong(Uri uri, params string[] notSongs)
    {
        var segments = uri.Segments.Select(s => s.Trim('/').ToLowerInvariant()).ToHashSet();
        if (notSongs.Any(segments.Contains))
            throw new ArcanistException.Conflict(ErrorCodes.WishLinkNotASong);
    }

    // Deezer's public API (https://developers.deezer.com/api/track): no key needed.
    private static async Task<Found?> DeezerAsync(HttpClient http, Uri uri, CancellationToken cancellationToken)
    {
        RequireSong(uri, "album", "playlist", "artist", "show", "episode", "profile");
        if (Segment(uri, "track") is not { } id || !long.TryParse(id, out _))
            throw new ArcanistException.Conflict(ErrorCodes.WishLinkNotASong);
        var track = await http.GetFromJsonAsync<JsonElement>($"https://api.deezer.com/track/{id}", cancellationToken);
        if (track.TryGetProperty("error", out _))
            return null;
        return new Found(Text(track, "title") ?? "", Text(track.GetPropertyOrDefault("artist"), "name") ?? "",
            Text(track.GetPropertyOrDefault("album"), "title"),
            track.TryGetProperty("duration", out var seconds) && seconds.TryGetInt32(out var s) ? s * 1000 : null,
            Text(track, "isrc"), ImageUrl: Text(track.GetPropertyOrDefault("album"), "cover_xl"));
    }

    // The iTunes Search API's lookup (https://performance-partners.apple.com/search-api): no key needed.
    private static async Task<Found?> AppleMusicAsync(HttpClient http, Uri uri, CancellationToken cancellationToken)
    {
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var id = query["i"] ?? (uri.Segments.Any(s => s.Trim('/') == "song") ? uri.Segments[^1].Trim('/') : null);
        if (id is null || !long.TryParse(id, out _))
            throw new ArcanistException.Conflict(ErrorCodes.WishLinkNotASong);
        var country = uri.Segments.Length > 1 && uri.Segments[1].Trim('/') is { Length: 2 } cc ? cc : "us";
        var body = await http.GetFromJsonAsync<JsonElement>($"https://itunes.apple.com/lookup?id={id}&country={country}&entity=song", cancellationToken);
        var track = body.GetPropertyOrDefault("results").EnumerateArrayOrEmpty().FirstOrDefault(r => Text(r, "wrapperType") == "track");
        if (track.ValueKind != JsonValueKind.Object)
            return null;
        return new Found(Text(track, "trackName") ?? "", Text(track, "artistName") ?? "", Text(track, "collectionName"),
            track.TryGetProperty("trackTimeMillis", out var ms) && ms.TryGetInt32(out var m) ? m : null,
            ImageUrl: Text(track, "artworkUrl100")?.Replace("100x100bb", "1000x1000bb", StringComparison.Ordinal));
    }

    // YouTube's oEmbed: the title, and the channel (a "Topic" channel is the artist's).
    private static async Task<Found?> YouTubeAsync(HttpClient http, Uri uri, CancellationToken cancellationToken)
    {
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var id = uri.Host.EndsWith("youtu.be", StringComparison.OrdinalIgnoreCase) ? uri.Segments.ElementAtOrDefault(1)?.Trim('/')
            : query["v"] ?? Segment(uri, "shorts") ?? Segment(uri, "live");
        if (string.IsNullOrEmpty(id))
            throw new ArcanistException.Conflict(ErrorCodes.WishLinkNotASong);
        var watch = $"https://www.youtube.com/watch?v={Uri.EscapeDataString(id)}";
        var body = await http.GetFromJsonAsync<JsonElement>($"https://www.youtube.com/oembed?format=json&url={Uri.EscapeDataString(watch)}", cancellationToken);
        var title = Text(body, "title") ?? "";
        var channel = Text(body, "author_name") ?? "";
        string artist;
        if (channel.EndsWith(" - Topic", StringComparison.Ordinal))
            artist = channel[..^" - Topic".Length];
        else if (title.IndexOf(" - ", StringComparison.Ordinal) is var dash and > 0)
        {
            artist = title[..dash];
            title = title[(dash + 3)..];
        }
        else
            artist = channel.EndsWith("VEVO", StringComparison.Ordinal) ? channel[..^4] : channel;

        return new Found(CleanTitle(title), artist.Trim(), ImageUrl: $"https://i.ytimg.com/vi/{Uri.EscapeDataString(id)}/hqdefault.jpg");
    }

    private async Task<Found?> MusicBrainzAsync(Uri uri, CancellationToken cancellationToken)
    {
        RequireSong(uri, "release", "release-group", "artist", "work", "label");
        if (!Guid.TryParse(Segment(uri, "recording"), out var mbid))
            throw new ArcanistException.Conflict(ErrorCodes.WishLinkNotASong);
        var http = clients.CreateClient(OnlineHttp.MusicBrainz);
        var recording = await gate.MusicBrainzAsync(() => http.GetFromJsonAsync<JsonElement>(
            $"https://musicbrainz.org/ws/2/recording/{mbid}?inc=artist-credits+releases&fmt=json", cancellationToken), cancellationToken);
        var release = recording.GetPropertyOrDefault("releases").EnumerateArrayOrEmpty().FirstOrDefault();
        return new Found(Text(recording, "title") ?? "", Credit(recording), release.ValueKind == JsonValueKind.Object ? Text(release, "title") : null,
            recording.TryGetProperty("length", out var length) && length.TryGetInt32(out var ms) ? ms : null,
            RecordingMbid: mbid, ReleaseMbid: Guid.TryParse(release.ValueKind == JsonValueKind.Object ? Text(release, "id") : null, out var r) ? r : null);
    }

    // SoundCloud's oEmbed: "Song by Artist".
    private static async Task<Found?> SoundCloudAsync(HttpClient http, Uri uri, CancellationToken cancellationToken)
    {
        RequireSong(uri, "sets", "albums", "tracks", "likes", "reposts", "followers");
        if (uri.Segments.Length < 3)
            throw new ArcanistException.Conflict(ErrorCodes.WishLinkNotASong);
        var body = await http.GetFromJsonAsync<JsonElement>($"https://soundcloud.com/oembed?format=json&url={Uri.EscapeDataString(uri.ToString())}", cancellationToken);
        var (title, artist) = SplitBy(Text(body, "title") ?? "", " by ");
        return new Found(title, artist ?? Text(body, "author_name") ?? "", ImageUrl: Text(body, "thumbnail_url"));
    }

    // Spotify's pages describe the song in their metadata: og:title, music:musician_description, og:description
    // ("Artist · Album · Song · 2001") and music:duration.
    private static async Task<Found?> SpotifyAsync(HttpClient http, Uri uri, CancellationToken cancellationToken)
    {
        RequireSong(uri, "album", "playlist", "artist", "show", "episode", "user", "collection");
        if (Segment(uri, "track") is not { Length: > 0 } id)
            throw new ArcanistException.Conflict(ErrorCodes.WishLinkNotASong);
        var page = await ReadPageAsync(http, new Uri($"https://open.spotify.com/track/{id}"), cancellationToken);
        var meta = Meta(page);
        var parts = meta.GetValueOrDefault("og:description")?.Split(" · ") ?? [];
        var artist = meta.GetValueOrDefault("music:musician_description") ?? parts.ElementAtOrDefault(0);
        var album = parts.Length >= 3 && parts[^2] is "Song" or "Canción" ? parts.ElementAtOrDefault(1) : null;
        return new Found(meta.GetValueOrDefault("og:title") ?? "", artist ?? "", album,
            int.TryParse(meta.GetValueOrDefault("music:duration"), out var seconds) ? seconds * 1000 : null,
            ImageUrl: meta.GetValueOrDefault("og:image"));
    }

    // Any other page: schema.org's MusicRecording (JSON-LD), then the Open Graph and music metadata.
    private static async Task<Found?> PageAsync(HttpClient http, Uri uri, CancellationToken cancellationToken)
    {
        var page = await ReadPageAsync(http, uri, cancellationToken);
        var meta = Meta(page);
        var image = meta.GetValueOrDefault("og:image");
        foreach (Match script in JsonLd().Matches(page))
        {
            JsonElement root;
            try
            {
                root = JsonDocument.Parse(WebUtility.HtmlDecode(script.Groups[1].Value)).RootElement;
            }
            catch (JsonException)
            {
                continue;
            }

            var recording = (root.ValueKind == JsonValueKind.Array ? root.EnumerateArray() : Enumerable.Repeat(root, 1))
                .Concat(root.GetPropertyOrDefault("@graph").EnumerateArrayOrEmpty())
                .FirstOrDefault(e => Text(e, "@type") == "MusicRecording");
            if (recording.ValueKind != JsonValueKind.Object)
                continue;
            var byArtist = recording.GetPropertyOrDefault("byArtist");
            var artist = byArtist.ValueKind == JsonValueKind.Array ? Text(byArtist.EnumerateArray().FirstOrDefault(), "name") : Text(byArtist, "name") ?? Text(recording, "byArtist");
            if (Text(recording, "name") is { Length: > 0 } name && artist is { Length: > 0 })
                return new Found(name, artist, Text(recording.GetPropertyOrDefault("inAlbum"), "name"), IsoDuration(Text(recording, "duration")),
                    Text(recording, "isrcCode"), ImageUrl: image);
        }

        var title = meta.GetValueOrDefault("og:title") ?? meta.GetValueOrDefault("twitter:title");
        if (title is null)
            return null;
        var musician = meta.GetValueOrDefault("music:musician_description");
        var (song, by) = musician is null ? SplitBy(title, ", by ", " by ", " - ") : (title, musician);
        return new Found(song, by ?? "", DurationMs: int.TryParse(meta.GetValueOrDefault("music:duration"), out var s) ? s * 1000 : null, ImageUrl: image);
    }

    private static async Task<string> ReadPageAsync(HttpClient http, Uri uri, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentType?.MediaType is { } type && !type.Contains("html", StringComparison.OrdinalIgnoreCase))
            throw new ArcanistException.Conflict(ErrorCodes.WishLinkUnreadable);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while (buffer.Length < MaxPageBytes && (read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            buffer.Write(chunk, 0, read);
        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>The page's meta tags by property or name (the first of each), HTML-decoded.</summary>
    internal static Dictionary<string, string> Meta(string page)
    {
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match tag in MetaTag().Matches(page))
        {
            var attributes = Attribute().Matches(tag.Value).ToDictionary(a => a.Groups[1].Value.ToLowerInvariant(), a => a.Groups[3].Value,
                StringComparer.OrdinalIgnoreCase);
            var key = attributes.GetValueOrDefault("property") ?? attributes.GetValueOrDefault("name");
            if (key is not null && attributes.TryGetValue("content", out var content) && content.Length > 0)
                meta.TryAdd(key, WebUtility.HtmlDecode(content).Trim());
        }

        return meta;
    }

    /// <summary>
    /// The recording and a release in MusicBrainz: by ISRC (exact), or else by title and artist when the service says
    /// the duration too (the same song is many recordings: remasters, edits, live ones; without the duration, none is
    /// taken).
    /// </summary>
    private async Task<(Guid? Recording, Guid? Release, bool Exact)> FindInMusicBrainzAsync(Found found, CancellationToken cancellationToken)
    {
        var http = clients.CreateClient(OnlineHttp.MusicBrainz);
        try
        {
            if (found.Isrc is { Length: > 0 } isrc && IsrcFormat().IsMatch(isrc))
            {
                JsonElement candidates = default;
                try
                {
                    var body = await gate.MusicBrainzAsync(() => http.GetFromJsonAsync<JsonElement>(
                        $"https://musicbrainz.org/ws/2/isrc/{isrc.ToUpperInvariant()}?inc=releases&fmt=json", cancellationToken), cancellationToken);
                    candidates = body.GetPropertyOrDefault("recordings");
                }
                catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound)
                {
                    // An ISRC MusicBrainz does not know: the search below may still find the song.
                }

                if (candidates.EnumerateArrayOrEmpty().Any())
                {
                    var (recording, release) = Ids(Best(candidates.EnumerateArrayOrEmpty()));
                    return (recording, release, true);
                }
            }

            if (found.DurationMs is not { } duration)
                return (null, null, false);

            var query = $"recording:\"{MusicBrainzClient.Escape(found.Title)}\" AND artist:\"{MusicBrainzClient.Escape(found.Artist)}\"";
            if (found.Album is { Length: > 0 } album)
                query += $" AND release:\"{MusicBrainzClient.Escape(album)}\"";
            var search = await gate.MusicBrainzAsync(() => http.GetFromJsonAsync<JsonElement>(
                $"https://musicbrainz.org/ws/2/recording/?query={Uri.EscapeDataString(query)}&limit=10&fmt=json", cancellationToken), cancellationToken);
            var matches = search.GetPropertyOrDefault("recordings").EnumerateArrayOrEmpty()
                .Where(r => r.TryGetProperty("score", out var score) && score.GetInt32() >= 90)
                .Where(r => r.TryGetProperty("length", out var length) && length.TryGetInt32(out var ms) && Math.Abs(ms - duration) <= 2000)
                .ToList();
            if (matches.Count == 0)
                return (null, null, false);
            var (likely, likelyRelease) = Ids(Best(matches));
            return (likely, likelyRelease, false);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // MusicBrainz is a bonus: the wish is still good without it.
            logger.LogInformation("Could not look up {Title} in MusicBrainz: {Error}", found.Title, e.Message);
            return (null, null, false);
        }

        // The same song is several recordings (remasters, edits, live…): the one whose best release ranks first.
        static (JsonElement Recording, JsonElement Release) Best(IEnumerable<JsonElement> recordings) =>
            recordings.Select(r => (Recording: r, Release: BestRelease(r)))
                .OrderBy(x => Rank(x.Release))
                .ThenBy(x => Date(x.Release))
                .First();

        // The cover people know: an official album, not a compilation or a live one (no secondary type), the
        // earliest; then an official single or EP; then whatever there is.
        static JsonElement BestRelease(JsonElement recording) =>
            recording.GetPropertyOrDefault("releases").EnumerateArrayOrEmpty().OrderBy(Rank).ThenBy(Date).FirstOrDefault();

        static int Rank(JsonElement release)
        {
            if (release.ValueKind != JsonValueKind.Object)
                return 4;
            var group = release.GetPropertyOrDefault("release-group");
            var official = Text(release, "status") == "Official";
            var plain = !group.GetPropertyOrDefault("secondary-types").EnumerateArrayOrEmpty().Any();
            return (official, plain, Text(group, "primary-type")) switch
            {
                (true, true, "Album") => 0,
                (true, true, "Single" or "EP") => 1,
                (true, _, _) => 2,
                _ => 3,
            };
        }

        static string Date(JsonElement release) =>
            release.ValueKind == JsonValueKind.Object && Text(release, "date") is { Length: >= 4 } date ? date : "9999";

        static (Guid?, Guid?) Ids((JsonElement Recording, JsonElement Release) best) =>
            (Guid.TryParse(Text(best.Recording, "id"), out var r) ? r : null,
                best.Release.ValueKind == JsonValueKind.Object && Guid.TryParse(Text(best.Release, "id"), out var rel) ? rel : null);
    }

    /// <summary>
    /// The cover in the artwork cache: the Cover Art Archive's for the release and the service's image, in that order
    /// when the recording is exact, the other way round when it is only likely.
    /// </summary>
    private async Task<Guid?> ArtworkAsync(HttpClient http, Guid? release, string? imageUrl, bool coverArtFirst, CancellationToken cancellationToken)
    {
        async Task<byte[]?> CoverArtAsync()
        {
            try
            {
                return release is { } releaseMbid ? await coverArt.FrontAsync(releaseMbid, cancellationToken) : null;
            }
            catch (OnlineServiceException)
            {
                return null;
            }
        }

        async Task<byte[]?> ServiceImageAsync()
        {
            if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var source) || source.Scheme != "https")
                return null;
            try
            {
                using var response = await http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                return response.IsSuccessStatusCode && response.Content.Headers.ContentLength is null or <= MaxImageBytes
                    ? await response.Content.ReadAsByteArrayAsync(cancellationToken)
                    : null;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }

        var image = coverArtFirst
            ? await CoverArtAsync() ?? await ServiceImageAsync()
            : await ServiceImageAsync() ?? await CoverArtAsync();

        if (image is not { Length: > 0 and <= MaxImageBytes } || artworkStore.Store(image) is not { } stored)
            return null;
        var existing = await db.Artworks.Where(a => a.Hash == stored.Hash).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
            return existing;
        var artwork = new Data.Entities.Artwork { Hash = stored.Hash, MimeType = stored.MimeType, Width = stored.Width, Height = stored.Height, Source = ArtworkSource.Online };
        db.Artworks.Add(artwork);
        await db.SaveChangesAsync(cancellationToken);
        return artwork.Id;
    }

    private static string Credit(JsonElement recording) =>
        string.Concat(recording.GetPropertyOrDefault("artist-credit").EnumerateArrayOrEmpty()
            .Select(c => (Text(c, "name") ?? "") + (Text(c, "joinphrase") ?? "")));

    private static (string Title, string? Artist) SplitBy(string text, params string[] separators)
    {
        foreach (var separator in separators)
        {
            var at = text.LastIndexOf(separator, StringComparison.Ordinal);
            if (at <= 0)
                continue;
            // "Artist - Song" puts the artist first; "Song by Artist", after.
            return separator == " - " ? (text[(at + separator.Length)..].Trim(), text[..at].Trim()) : (text[..at].Trim(), text[(at + separator.Length)..].Trim());
        }

        return (text.Trim(), null);
    }

    /// <summary>Video titles carry "(Official Video)", "[Lyrics]"…: not part of the song's name.</summary>
    internal static string CleanTitle(string title) => VideoNoise().Replace(title, "").Trim();

    private static int? IsoDuration(string? value)
    {
        try
        {
            return value is { Length: > 0 } ? (int)XmlConvert.ToTimeSpan(value).TotalMilliseconds : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Clip(string value) => value.Length > WishListService.TextMaxLength ? value[..WishListService.TextMaxLength] : value;

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase)]
    private static partial Regex LinkInText();

    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaTag();

    [GeneratedRegex(@"([\w:-]+)\s*=\s*(""|')(.*?)\2", RegexOptions.Singleline)]
    private static partial Regex Attribute();

    [GeneratedRegex(@"<script[^>]*type\s*=\s*[""']application/ld\+json[""'][^>]*>(.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex JsonLd();

    [GeneratedRegex(@"^[A-Za-z]{2}[A-Za-z0-9]{3}\d{7}$")]
    private static partial Regex IsrcFormat();

    [GeneratedRegex(@"\s*[\(\[][^\)\]]*\b(official|video|audio|lyrics?|letra|visuali[sz]er|mv|hd|4k|remaster(ed)?\s*\d*)\b[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex VideoNoise();
}

internal static class WishLinkJson
{
    public static JsonElement GetPropertyOrDefault(this JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) ? value : default;

    public static IEnumerable<JsonElement> EnumerateArrayOrEmpty(this JsonElement element) =>
        element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : [];
}

/// <summary>
/// MusicBrainz asks for one request per second per application: the links read for wishes take turns (a singleton),
/// and a 503 (too fast) is tried once more after the interval.
/// </summary>
public sealed class WishLinkGate(IOptions<OnlineOptions> options, TimeProvider time)
{
    private readonly RateGate _musicBrainz = new(options.Value.RequestInterval, time);

    public async Task<T> MusicBrainzAsync<T>(Func<Task<T>> request, CancellationToken cancellationToken)
    {
        await _musicBrainz.WaitAsync(cancellationToken);
        try
        {
            return await request();
        }
        catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests)
        {
            await _musicBrainz.WaitAsync(cancellationToken);
            return await request();
        }
    }
}
