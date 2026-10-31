using System.Text.Json.Nodes;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Organization;
using Arcanist.Core.Hosting;
using Arcanist.Core.Organization;

namespace Arcanist.Core.Cloud;

/// <summary>
/// Downloads a track of the linked Ultimate Cloud account to a local file, tagged again: Ultimate Cloud keeps the audio
/// without tags, and describes it apart (title, artists, album, cover…). The tags are written with ATL (<see cref="TagWriter"/>).
/// </summary>
public sealed class CloudTrackDownloader(
    UltimateCloudClient client,
    IHttpClientFactory clients,
    ArcanistPaths paths,
    ILogger<CloudTrackDownloader> logger)
{
    /// <summary>What a download asks Ultimate Cloud about each track.</summary>
    public const string Fields =
        "cloudTrackId title artist album genre trackNumber discNumber year beatsPerMinute audioFormat " +
        "coverImageId musicBrainzRecordingId musicBrainzReleaseGroupId musicBrainzArtistId";

    /// <summary>Covers larger than this are left out.</summary>
    private const long MaxCoverBytes = 10 * 1024 * 1024;

    /// <summary>Writes the track to <paramref name="file"/>.</summary>
    /// <param name="track">The track as Ultimate Cloud describes it (<see cref="Fields"/>).</param>
    /// <returns>Null when it is there; otherwise why not (<c>download_failed</c>, <c>not_found</c>).</returns>
    public async Task<string?> DownloadAsync(JsonNode track, string id, string file, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(paths.Cache, "sync");
        Directory.CreateDirectory(folder);
        string? cover = null;
        try
        {
            var url = (await client.SendAsync(
                "mutation Download($id: String!) { downloadUrl(trackId: $id) { url } }", new { id }, cancellationToken))["downloadUrl"]!["url"]!.GetValue<string>();
            if (!await FetchAsync(url, file, cancellationToken))
                return ErrorCodes.DownloadFailed;

            if (Text(track, "coverImageId") is { } coverId)
            {
                var coverUrl = (await client.SendAsync("query Cover($id: String!) { coverImageUrl(coverImageId: $id) { url } }", new { id = coverId }, cancellationToken))
                    ["coverImageUrl"]?["url"]?.GetValue<string>();
                cover = Path.Combine(folder, $"{Guid.NewGuid():N}.img");
                if (coverUrl is null || !await FetchAsync(coverUrl, cover, cancellationToken))
                    cover = null;
            }

            int? Int(string name) => track[name] is JsonValue value && value.TryGetValue<int>(out var number) && number > 0 ? number : null;
            Guid? Mbid(string name) => Guid.TryParse(Text(track, name), out var mbid) ? mbid : null;
            try
            {
                var picture = cover is not null && new FileInfo(cover).Length <= MaxCoverBytes ? await File.ReadAllBytesAsync(cover, cancellationToken) : null;
                TagWriter.Write(file, new ImportTagsInput(
                    Text(track, "title"), Text(track, "artist"), Text(track, "album"), null, Int("year"), Int("trackNumber"), Int("discNumber"),
                    Text(track, "genre"), Mbid("musicBrainzRecordingId")), picture);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or NotSupportedException)
            {
                // Untagged, the song is still the song: it goes on as it is.
                logger.LogDebug("Could not tag {Title} from Ultimate Cloud: {Error}", Text(track, "title"), e.Message);
            }

            return null;
        }
        catch (UltimateCloudException e) when (e.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return ErrorCodes.NotFound;
        }
        finally
        {
            if (cover is not null)
                File.Delete(cover);
        }
    }

    private async Task<bool> FetchAsync(string url, string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await clients.CreateClient(UltimateCloudStorage.FilesClientName).GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return false;
            await using var target = File.Create(path);
            await response.Content.CopyToAsync(target, cancellationToken);
            return true;
        }
        catch (HttpRequestException e)
        {
            logger.LogDebug(e, "Downloading from Ultimate Cloud failed");
            return false;
        }
    }

    internal static string? Text(JsonNode track, string name) =>
        track[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
}
