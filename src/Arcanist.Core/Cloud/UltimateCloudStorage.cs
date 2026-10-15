using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Arcanist.Core.Application;
using Arcanist.Core.Capabilities;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;

namespace Arcanist.Core.Cloud;

/// <summary>Which of the account's collections a library shows.</summary>
public enum UltimateCloudCollection
{
    /// <summary>The account's library (what Ultimate Desktop and the Web Player upload).</summary>
    Library,

    /// <summary>Cloud Drop: music dropped into the account, kept apart from the library.</summary>
    CloudDrop,
}

/// <summary>
/// The music an Ultimate Cloud account keeps, as a library source (<see cref="LibrarySourceKind.UltimateCloud"/>; its
/// location is the <see cref="UltimateCloudCollection"/>). Nothing is downloaded to catalog it: Ultimate Cloud
/// describes every track, and audio is fetched when someone plays it, through short-lived signed URLs. Paths are the
/// track's id and format, which never change. Read only; unavailable while no account is linked.
/// </summary>
internal sealed class UltimateCloudStorage(
    UltimateCloudClient client,
    UltimateCloudAuth auth,
    ICloudLinkState link,
    IHttpClientFactory clients,
    TimeProvider time,
    ILogger<UltimateCloudStorage> logger) : ILibraryStorage, IDescribedStorage
{
    public const string FilesClientName = "ultimate-cloud-files";

    private readonly ConcurrentDictionary<string, (string Url, DateTimeOffset Until)> _urls = new(StringComparer.Ordinal);

    public LibrarySourceKind Kind => LibrarySourceKind.UltimateCloud;

    public string NormalizeLocation(string location) =>
        Enum.TryParse<UltimateCloudCollection>(location.Trim(), ignoreCase: true, out var collection) ? collection.ToString() : location.Trim();

    public async Task<StorageProbe> ProbeAsync(StorageLocation location, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<UltimateCloudCollection>(location.Location, out _))
            return new StorageProbe(StorageProblem.Invalid);
        if (link.Tier < CloudTier.Free || await auth.LinkAsync(cancellationToken) is null)
            return new StorageProbe(StorageProblem.NotFound);
        try
        {
            await client.SendAsync("query Probe { myLibrary { id } }", null, cancellationToken);
            return StorageProbe.Available;
        }
        catch (UltimateCloudException e)
        {
            logger.LogDebug("Ultimate Cloud is not available as a library: {Error}", e.Code);
            return new StorageProbe(e.Code == ErrorCodes.CloudLinkExpired ? StorageProblem.AccessDenied : StorageProblem.NotFound);
        }
    }

    public async IAsyncEnumerable<DescribedFile> DescribeAsync(StorageLocation location, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var field = Enum.Parse<UltimateCloudCollection>(location.Location) == UltimateCloudCollection.CloudDrop ? "cloudDropTracks" : "library";
        JsonNode data;
        try
        {
            data = await client.SendAsync($"query Tracks {{ {field} {{ {TrackFields} }} }}", null, cancellationToken);
        }
        catch (UltimateCloudException e)
        {
            // The scanner takes it as an unavailable source and marks nothing as missing.
            throw new IOException(e.Code, e);
        }

        foreach (var track in data[field]?.AsArray() ?? [])
        {
            if (track is not null && Describe(track) is { } file)
                yield return file;
        }
    }

    /// <summary>The fields of <c>CloudTrackPayload</c> the catalog uses.</summary>
    internal const string TrackFields =
        "cloudTrackId title artist album genre trackNumber discNumber year duration audioBitrate audioSampleRate audioFormat size " +
        "coverImageId musicBrainzRecordingId musicBrainzArtistId dateUpdated";

    internal static DescribedFile? Describe(JsonNode track)
    {
        var id = track["cloudTrackId"]?.GetValue<string>();
        if (string.IsNullOrEmpty(id))
            return null;

        var artists = (Text(track, "artist") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var metadata = new AudioMetadata
        {
            Title = Text(track, "title"),
            Artists = artists,
            Album = Text(track, "album"),
            Genres = Text(track, "genre") is { } genre ? [genre] : [],
            Year = Int(track, "year"),
            TrackNumber = Int(track, "trackNumber"),
            DiscNumber = Int(track, "discNumber"),
            RecordingMbid = Guid.TryParse(Text(track, "musicBrainzRecordingId"), out var recording) ? recording : null,
            ArtistMbid = Guid.TryParse(Text(track, "musicBrainzArtistId"), out var artist) ? artist : null,
            Format = Text(track, "audioFormat"),
            BitRate = Int(track, "audioBitrate") is > 0 and var bitRate ? bitRate : null,
            SampleRate = Int(track, "audioSampleRate") is > 0 and var sampleRate ? sampleRate : null,
            DurationMs = (int)Math.Round((track["duration"]?.GetValue<double>() ?? 0) * 1000),
        };

        return new DescribedFile(
            id + Extension(metadata.Format),
            0,
            null,
            null,
            track["size"]?.GetValue<long>() ?? 0,
            track["dateUpdated"]?.GetValue<DateTime>().ToUniversalTime() ?? DateTime.UnixEpoch,
            HashOf(id),
            Text(track, "coverImageId"),
            metadata);
    }

    /// <summary>
    /// Ultimate Cloud does not tell the audio hash of a track, so its catalog hash is derived from its id: stable, and
    /// never equal to the hash of a local file (copies of a song are recognized by album, disc, number and title).
    /// </summary>
    internal static string HashOf(string cloudTrackId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("ultimate-cloud:" + cloudTrackId)));

    /// <summary>The extension for Ultimate Cloud's audio format, so the scanner and players know what they get.</summary>
    internal static string Extension(string? format) => format?.Trim().ToLowerInvariant() switch
    {
        "flac" => ".flac",
        "mp3" or "mpeg" => ".mp3",
        "m4a" or "mp4" or "aac" or "alac" => ".m4a",
        "ogg" or "vorbis" => ".ogg",
        "opus" => ".opus",
        "wav" => ".wav",
        "aiff" or "aif" => ".aiff",
        "wma" => ".wma",
        _ => ".mp3",
    };

    public async Task<byte[]?> ReadArtworkAsync(StorageLocation location, string artwork, CancellationToken cancellationToken)
    {
        try
        {
            var data = await client.SendAsync("query Cover($id: String!) { coverImageUrl(coverImageId: $id) { url } }", new { id = artwork }, cancellationToken);
            if (data["coverImageUrl"]?["url"]?.GetValue<string>() is not { } url)
                return null;
            using var response = await Files.GetAsync(url, cancellationToken);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(cancellationToken) : null;
        }
        catch (Exception e) when (e is UltimateCloudException or HttpRequestException)
        {
            logger.LogDebug("Could not read the cover {Cover} from Ultimate Cloud: {Error}", artwork, e.Message);
            return null;
        }
    }

    /// <summary>A signed URL of the track's audio, reused while it is valid.</summary>
    /// <exception cref="FileNotFoundException">The track is gone from Ultimate Cloud.</exception>
    /// <exception cref="IOException">Ultimate Cloud could not be reached, or refuses (no account, a lapsed subscription…).</exception>
    public async Task<string> AudioUrlAsync(string relativePath, CancellationToken cancellationToken)
    {
        var id = AudioFormats.WithoutExtension(relativePath);
        if (_urls.TryGetValue(id, out var known) && time.GetUtcNow() < known.Until)
            return known.Url;

        JsonNode data;
        try
        {
            data = await client.SendAsync("mutation Stream($id: String!) { streamUrl(trackId: $id) { url validForSeconds } }", new { id }, cancellationToken);
        }
        catch (UltimateCloudException e) when (e.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException(e.Message, relativePath);
        }
        catch (UltimateCloudException e)
        {
            throw new IOException(e.Code, e);
        }

        var url = data["streamUrl"]?["url"]?.GetValue<string>() ?? throw new IOException(ErrorCodes.CloudUnreachable);
        var valid = data["streamUrl"]?["validForSeconds"]?.GetValue<int>() ?? 0;
        // Renewed a minute early, so a request never starts with a URL about to expire.
        if (valid > 120)
            _urls[id] = (url, time.GetUtcNow().AddSeconds(valid - 60));
        return url;
    }

    /// <summary>Drops the cached URL of a track (it was refused before its time).</summary>
    public void Forget(string relativePath) => _urls.TryRemove(AudioFormats.WithoutExtension(relativePath), out _);

    public HttpClient Files => clients.CreateClient(FilesClientName);

    public async IAsyncEnumerable<StorageEntry> EnumerateAsync(StorageLocation location, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var file in DescribeAsync(location, cancellationToken))
            yield return new StorageEntry(file.RelativePath, file.Size, file.ModifiedAt);
    }

    /// <summary>The whole audio, for conversions and analysis; players get it through <see cref="UltimateCloudStreaming"/>.</summary>
    public async Task<Stream> OpenReadAsync(StorageLocation location, string relativePath, CancellationToken cancellationToken)
    {
        var url = await AudioUrlAsync(relativePath, cancellationToken);
        var response = await Files.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            _urls.TryRemove(AudioFormats.WithoutExtension(relativePath), out _);
            throw response.StatusCode == System.Net.HttpStatusCode.NotFound
                ? new FileNotFoundException("Gone from Ultimate Cloud.", relativePath)
                : new IOException($"Ultimate Cloud answered {(int)response.StatusCode}.");
        }

        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    public string? TryGetLocalPath(StorageLocation location, string relativePath) => null;

    public bool Contains(string outer, string inner) => string.Equals(outer, inner, StringComparison.Ordinal);

    public StringComparer PathComparer => StringComparer.Ordinal;

    public Task<bool> CanWriteAsync(StorageLocation location, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<bool> ExistsAsync(StorageLocation location, string relativePath, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task MoveAsync(StorageLocation location, string from, string to, CancellationToken cancellationToken) => throw ReadOnly();

    public Task ImportAsync(StorageLocation location, string relativePath, string localPath, CancellationToken cancellationToken) => throw ReadOnly();

    public Task<string> TrashAsync(StorageLocation location, string relativePath, CancellationToken cancellationToken) => throw ReadOnly();

    public Task PurgeTrashAsync(StorageLocation location, DateTime before, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RemoveEmptyFoldersAsync(StorageLocation location, string relativeFolder, CancellationToken cancellationToken) => Task.CompletedTask;

    public IDisposable? Watch(StorageLocation location, Action changed) => null;

    private static NotSupportedException ReadOnly() => new("Ultimate Cloud libraries are read-only here.");

    private static string? Text(JsonNode track, string name) =>
        track[name]?.GetValue<string>() is { } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static int? Int(JsonNode track, string name) =>
        track[name] is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;
}
