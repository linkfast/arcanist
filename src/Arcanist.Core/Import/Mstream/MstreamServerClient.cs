using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Application;

namespace Arcanist.Core.Import.Mstream;

/// <summary>
/// Talks to an mStream server through its public API: to read its libraries and users (as an administrator) and,
/// when each user first signs in to Arcanist, to check their password and bring their data.
/// </summary>
internal sealed class MstreamServerClient(IHttpClientFactory clients)
{
    public const string HttpClientName = "mstream";

    /// <summary>The server's address, as given: with a scheme and without a trailing slash.</summary>
    public static Uri? ParseUrl(string? url) =>
        Uri.TryCreate(url?.Trim().TrimEnd('/'), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null;

    /// <summary>A token for this user, or null when mStream refuses the password.</summary>
    public async Task<string?> LoginAsync(Uri server, string userName, string password, CancellationToken cancellationToken)
    {
        var response = await SendAsync(() => Client.PostAsJsonAsync(Url(server, "/api/v1/auth/login"), new { username = userName, password }, cancellationToken));
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest)
            return null;
        var body = await ReadAsync(response, cancellationToken);
        return body.TryGetProperty("token", out var token) && token.ValueKind == JsonValueKind.String ? token.GetString() : null;
    }

    /// <summary>The server's libraries and users. Needs an administrator's token.</summary>
    public async Task<(IReadOnlyList<MstreamLibraryData> Libraries, IReadOnlyList<MstreamUserData> Users)> ReadAsync(
        Uri server, string token, CancellationToken cancellationToken)
    {
        var directories = await GetAsync(server, "/api/v1/admin/directories", token, cancellationToken);
        var users = await GetAsync(server, "/api/v1/admin/users", token, cancellationToken);

        var libraries = directories.ValueKind == JsonValueKind.Object
            ? directories.EnumerateObject()
                .Select(d => new MstreamLibraryData(d.Name, d.Value.TryGetProperty("root", out var root) && root.ValueKind == JsonValueKind.String ? root.GetString()! : ""))
                .ToList()
            : [];
        var accounts = users.ValueKind == JsonValueKind.Object
            ? users.EnumerateObject()
                .Where(u => u.Value.ValueKind == JsonValueKind.Object)
                .Select(u => new MstreamUserData(
                    u.Name,
                    u.Value.TryGetProperty("admin", out var admin) && admin.ValueKind == JsonValueKind.True,
                    u.Value.TryGetProperty("vpaths", out var vpaths) && vpaths.ValueKind == JsonValueKind.Array
                        ? vpaths.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList()
                        : []))
                .ToList()
            : [];
        return (libraries, accounts);
    }

    /// <summary>The signed-in user's playlists, ratings and play counts.</summary>
    public async Task<MstreamUserContent> ReadUserAsync(Uri server, string token, CancellationToken cancellationToken)
    {
        var playlists = new List<MstreamPlaylistData>();
        var all = await GetAsync(server, "/api/v1/playlist/getall", token, cancellationToken);
        foreach (var name in all.ValueKind == JsonValueKind.Array
                     ? all.EnumerateArray().Select(p => p.TryGetProperty("name", out var n) ? n.GetString() : null).OfType<string>().Distinct()
                     : [])
        {
            var songs = await PostAsync(server, "/api/v1/playlist/load", new { playlistname = name }, token, cancellationToken);
            playlists.Add(new MstreamPlaylistData(name, Paths(songs).Select(s => s.Path).ToList()));
        }

        var tracks = new Dictionary<string, MstreamTrackData>(StringComparer.Ordinal);
        foreach (var (path, metadata) in Paths(await GetAsync(server, "/api/v1/db/rated", token, cancellationToken)))
            tracks[path] = new MstreamTrackData(path, Number(metadata, "rating"), 0, null);
        var played = await PostAsync(server, "/api/v1/db/stats/most-played", new { limit = 100_000 }, token, cancellationToken);
        foreach (var (path, metadata) in Paths(played))
        {
            var lastPlayed = metadata.TryGetProperty("last-played", out var value)
                ? MstreamFolder.ParseTime(value.ValueKind switch
                {
                    JsonValueKind.Number => value.GetDouble(),
                    JsonValueKind.String => value.GetString(),
                    _ => null,
                })
                : null;
            tracks[path] = (tracks.GetValueOrDefault(path) ?? new MstreamTrackData(path, null, 0, null)) with
            {
                PlayCount = Number(metadata, "play-count") ?? 0,
                LastPlayedAt = lastPlayed,
            };
        }

        return new MstreamUserContent(playlists, tracks.Values.ToList());
    }

    private HttpClient Client => clients.CreateClient(HttpClientName);

    private static Uri Url(Uri server, string path) => new(server.AbsoluteUri.TrimEnd('/') + path);

    private async Task<JsonElement> GetAsync(Uri server, string path, string token, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Url(server, path));
        request.Headers.Add("x-access-token", token);
        return await ReadAsync(await SendAsync(() => Client.SendAsync(request, cancellationToken)), cancellationToken);
    }

    private async Task<JsonElement> PostAsync(Uri server, string path, object body, string token, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Url(server, path)) { Content = JsonContent.Create(body) };
        request.Headers.Add("x-access-token", token);
        return await ReadAsync(await SendAsync(() => Client.SendAsync(request, cancellationToken)), cancellationToken);
    }

    private static async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send)
    {
        try
        {
            return await send();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new ArcanistException.Conflict(ErrorCodes.MstreamUnreachable);
        }
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.MethodNotAllowed or HttpStatusCode.Unauthorized)
            throw new ArcanistException.Forbidden(ErrorCodes.MstreamAdminRequired);
        if (!response.IsSuccessStatusCode)
            throw new ArcanistException.Conflict(ErrorCodes.MstreamUnreachable);
        try
        {
            return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        }
        catch (JsonException)
        {
            throw new ArcanistException.Conflict(ErrorCodes.MstreamUnreachable);
        }
    }

    /// <summary>The <c>{filepath, metadata}</c> objects of a list.</summary>
    private static IEnumerable<(string Path, JsonElement Metadata)> Paths(JsonElement list) =>
        list.ValueKind != JsonValueKind.Array
            ? []
            : list.EnumerateArray()
                .Where(i => i.ValueKind == JsonValueKind.Object && i.TryGetProperty("filepath", out var p) && p.ValueKind == JsonValueKind.String)
                .Select(i => (i.GetProperty("filepath").GetString()!,
                    i.TryGetProperty("metadata", out var m) && m.ValueKind == JsonValueKind.Object ? m : default));

    private static int? Number(JsonElement metadata, string name) =>
        metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? (int)Math.Round(value.GetDouble())
            : null;
}
