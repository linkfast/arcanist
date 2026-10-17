using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;

namespace Arcanist.Core.Online;

/// <summary>A request to an online service failed; <see cref="Code"/> is what the admin panel shows.</summary>
public sealed class OnlineServiceException(string code, Exception? inner = null) : Exception(code, inner)
{
    public string Code { get; } = code;
}

/// <summary>Named HTTP clients for the online services, all identifying Arcanist as their terms ask.</summary>
public static class OnlineHttp
{
    public const string MusicBrainz = "musicbrainz";
    public const string AcoustId = "acoustid";
    public const string CoverArtArchive = "coverartarchive";
    public const string LrcLib = "lrclib";

    /// <summary>MusicBrainz and LRCLib want the application, its version and a way to reach its authors.</summary>
    public static string UserAgent(string? repositoryUrl) =>
        $"Arcanist/{typeof(OnlineHttp).Assembly.GetName().Version?.ToString(3)} ( {repositoryUrl ?? "https://github.com/linkfast/arcanist"} )";

    public static async Task<T> SendAsync<T>(Func<Task<T>> request)
    {
        try
        {
            return await request();
        }
        catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
        {
            throw new OnlineServiceException("rate_limited", e);
        }
        catch (HttpRequestException e)
        {
            throw new OnlineServiceException("connection_failed", e);
        }
        catch (TaskCanceledException e)
        {
            throw new OnlineServiceException("timeout", e);
        }
        catch (System.Text.Json.JsonException e)
        {
            throw new OnlineServiceException("unexpected_response", e);
        }
    }
}

/// <summary>AcoustID lookups by fingerprint (https://acoustid.org/webservice).</summary>
public sealed class AcoustIdClient(IHttpClientFactory clients)
{
    public sealed record Match(Guid AcoustId, double Score, IReadOnlyList<Guid> RecordingMbids);

    public Task<IReadOnlyList<Match>> LookupAsync(string key, string fingerprint, int durationSeconds, CancellationToken cancellationToken) =>
        OnlineHttp.SendAsync<IReadOnlyList<Match>>(async () =>
        {
            var http = clients.CreateClient(OnlineHttp.AcoustId);
            // POST: fingerprints are too long for a URL.
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client"] = key,
                ["meta"] = "recordings",
                ["duration"] = durationSeconds.ToString(CultureInfo.InvariantCulture),
                ["fingerprint"] = fingerprint,
                ["format"] = "json",
            });
            using var response = await http.PostAsync("https://api.acoustid.org/v2/lookup", form, cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<Response>(cancellationToken) ?? throw new OnlineServiceException("unexpected_response");
            if (body.Status != "ok")
            {
                // Error codes 4 and 5 are an invalid or missing application key.
                throw new OnlineServiceException(body.Error?.Code is 4 or 5 ? "invalid_key" : "service_error");
            }

            return (body.Results ?? [])
                .Where(r => Guid.TryParse(r.Id, out _))
                .Select(r => new Match(Guid.Parse(r.Id), r.Score,
                    (r.Recordings ?? []).Select(x => Guid.TryParse(x.Id, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).Distinct().ToList()))
                .ToList();
        });

    /// <summary>A recording a fingerprint matched, with what MusicBrainz knows of it.</summary>
    public sealed record RecordingDetails(Guid AcoustId, double Score, Guid RecordingMbid, string? Title, string? Artist, IReadOnlyList<ReleaseDetails> Releases);

    /// <param name="DiscCount">How many discs the release has.</param>
    public sealed record ReleaseDetails(Guid ReleaseMbid, string? Title, string? Artist, int? Year, int? TrackNumber, int? DiscNumber, int DiscCount, string? TrackTitle);

    /// <summary>
    /// Looks up a fingerprint with the recordings it matched, their artists and the releases they are on, with the
    /// disc and number of the track: what is needed to suggest tags for a file.
    /// </summary>
    public Task<IReadOnlyList<RecordingDetails>> LookupDetailsAsync(string key, string fingerprint, int durationSeconds, CancellationToken cancellationToken) =>
        OnlineHttp.SendAsync<IReadOnlyList<RecordingDetails>>(async () =>
        {
            var http = clients.CreateClient(OnlineHttp.AcoustId);
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client"] = key,
                ["meta"] = "recordings releases tracks compress",
                ["duration"] = durationSeconds.ToString(CultureInfo.InvariantCulture),
                ["fingerprint"] = fingerprint,
                ["format"] = "json",
            });
            using var response = await http.PostAsync("https://api.acoustid.org/v2/lookup", form, cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<DetailsResponse>(cancellationToken) ?? throw new OnlineServiceException("unexpected_response");
            if (body.Status != "ok")
                throw new OnlineServiceException(body.Error?.Code is 4 or 5 ? "invalid_key" : "service_error");

            static string? Names(List<ArtistJson>? artists) =>
                artists is { Count: > 0 } ? string.Concat(artists.Select((a, i) => a.Name + (i < artists.Count - 1 ? a.JoinPhrase ?? "; " : ""))).Trim() : null;

            var found = new List<RecordingDetails>();
            foreach (var result in body.Results ?? [])
            {
                if (!Guid.TryParse(result.Id, out var acoustId))
                    continue;
                foreach (var recording in result.Recordings ?? [])
                {
                    if (!Guid.TryParse(recording.Id, out var recordingMbid))
                        continue;
                    var releases = new List<ReleaseDetails>();
                    foreach (var release in recording.Releases ?? [])
                    {
                        if (!Guid.TryParse(release.Id, out var releaseMbid))
                            continue;
                        // The medium and track of this recording on the release (the only ones AcoustID includes).
                        var medium = release.Mediums?.FirstOrDefault(m => m.Tracks is { Count: > 0 });
                        var track = medium?.Tracks?.FirstOrDefault();
                        releases.Add(new ReleaseDetails(releaseMbid, release.Title, Names(release.Artists), release.Date?.Year,
                            track?.Position, medium?.Position, release.MediumCount ?? 1, track?.Title));
                    }

                    found.Add(new RecordingDetails(acoustId, result.Score, recordingMbid, recording.Title, Names(recording.Artists), releases));
                }
            }

            return found;
        });

    private sealed record Response(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("results")] List<Result>? Results,
        [property: JsonPropertyName("error")] Error? Error);

    private sealed record DetailsResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("results")] List<DetailsResult>? Results,
        [property: JsonPropertyName("error")] Error? Error);

    private sealed record DetailsResult(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("score")] double Score,
        [property: JsonPropertyName("recordings")] List<RecordingJson>? Recordings);

    private sealed record RecordingJson(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("artists")] List<ArtistJson>? Artists,
        [property: JsonPropertyName("releases")] List<ReleaseJson>? Releases);

    private sealed record ArtistJson(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("joinphrase")] string? JoinPhrase);

    private sealed record ReleaseJson(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("artists")] List<ArtistJson>? Artists,
        [property: JsonPropertyName("date")] DateJson? Date,
        [property: JsonPropertyName("medium_count")] int? MediumCount,
        [property: JsonPropertyName("mediums")] List<MediumJson>? Mediums);

    private sealed record DateJson([property: JsonPropertyName("year")] int? Year);

    private sealed record MediumJson(
        [property: JsonPropertyName("position")] int? Position,
        [property: JsonPropertyName("tracks")] List<TrackJson>? Tracks);

    private sealed record TrackJson(
        [property: JsonPropertyName("position")] int? Position,
        [property: JsonPropertyName("title")] string? Title);

    private sealed record Result(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("score")] double Score,
        [property: JsonPropertyName("recordings")] List<Recording>? Recordings);

    private sealed record Recording([property: JsonPropertyName("id")] string Id);

    private sealed record Error([property: JsonPropertyName("code")] int Code);
}

/// <summary>MusicBrainz release search (https://musicbrainz.org/doc/MusicBrainz_API/Search).</summary>
public sealed class MusicBrainzClient(IHttpClientFactory clients)
{
    public sealed record ArtistCredit(Guid Id, string Name);

    public sealed record Release(Guid Id, string Title, int Score, int? TrackCount, string? Date, IReadOnlyList<ArtistCredit> Artists);

    public Task<IReadOnlyList<Release>> SearchReleasesAsync(string title, string artist, CancellationToken cancellationToken) =>
        OnlineHttp.SendAsync<IReadOnlyList<Release>>(async () =>
        {
            var http = clients.CreateClient(OnlineHttp.MusicBrainz);
            var query = $"release:\"{Escape(title)}\" AND artist:\"{Escape(artist)}\"";
            var url = $"https://musicbrainz.org/ws/2/release/?query={Uri.EscapeDataString(query)}&limit=5&fmt=json";
            var body = await http.GetFromJsonAsync<Response>(url, cancellationToken) ?? throw new OnlineServiceException("unexpected_response");

            return (body.Releases ?? [])
                .Where(r => Guid.TryParse(r.Id, out _))
                .Select(r => new Release(Guid.Parse(r.Id), r.Title ?? "", r.Score, r.TrackCount, r.Date,
                    (r.ArtistCredit ?? []).Where(c => c.Artist is not null && Guid.TryParse(c.Artist.Id, out _))
                        .Select(c => new ArtistCredit(Guid.Parse(c.Artist!.Id), c.Artist.Name ?? c.Name ?? "")).ToList()))
                .ToList();
        });

    /// <summary>Escapes Lucene query syntax inside a quoted phrase.</summary>
    internal static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is '\\' or '"')
                builder.Append('\\');
            builder.Append(c);
        }

        return builder.ToString();
    }

    private sealed record Response([property: JsonPropertyName("releases")] List<ReleaseJson>? Releases);

    private sealed record ReleaseJson(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("score")] int Score,
        [property: JsonPropertyName("track-count")] int? TrackCount,
        [property: JsonPropertyName("date")] string? Date,
        [property: JsonPropertyName("artist-credit")] List<CreditJson>? ArtistCredit);

    private sealed record CreditJson(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("artist")] ArtistJson? Artist);

    private sealed record ArtistJson(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string? Name);
}

/// <summary>Front covers from the Cover Art Archive (https://musicbrainz.org/doc/Cover_Art_Archive/API).</summary>
public sealed class CoverArtClient(IHttpClientFactory clients)
{
    private const long MaxBytes = 15 * 1024 * 1024;

    /// <summary>The release's front cover (at most 1200 px), or null if it has none.</summary>
    public Task<byte[]?> FrontAsync(Guid releaseMbid, CancellationToken cancellationToken) =>
        OnlineHttp.SendAsync(async () =>
        {
            var http = clients.CreateClient(OnlineHttp.CoverArtArchive);
            using var response = await http.GetAsync($"https://coverartarchive.org/release/{releaseMbid}/front-1200", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxBytes)
                throw new OnlineServiceException("unexpected_response");
            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        });
}

/// <summary>Lyrics from LRCLib (https://lrclib.net/docs).</summary>
public sealed class LrcLibClient(IHttpClientFactory clients)
{
    public sealed record Result(string? Plain, string? Synced, bool Instrumental);

    /// <summary>Lyrics for an exact track (LRCLib matches the duration within a couple of seconds), or null.</summary>
    public Task<Result?> GetAsync(string artist, string title, string? album, int durationSeconds, CancellationToken cancellationToken) =>
        OnlineHttp.SendAsync(async () =>
        {
            var http = clients.CreateClient(OnlineHttp.LrcLib);
            var url = $"https://lrclib.net/api/get?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(title)}" +
                      (album is null ? "" : $"&album_name={Uri.EscapeDataString(album)}") +
                      $"&duration={durationSeconds.ToString(CultureInfo.InvariantCulture)}";
            using var response = await http.GetAsync(url, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<Response>(cancellationToken) ?? throw new OnlineServiceException("unexpected_response");
            return new Result(Blank(body.PlainLyrics), Blank(body.SyncedLyrics), body.Instrumental);
        });

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Replace("\r\n", "\n").Trim();

    private sealed record Response(
        [property: JsonPropertyName("plainLyrics")] string? PlainLyrics,
        [property: JsonPropertyName("syncedLyrics")] string? SyncedLyrics,
        [property: JsonPropertyName("instrumental")] bool Instrumental);
}
