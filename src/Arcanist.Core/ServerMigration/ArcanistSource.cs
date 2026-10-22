using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Application;
using Arcanist.Core.Hosting;
using Arcanist.Core.P2P;

namespace Arcanist.Core.ServerMigration;

/// <summary>Where another Arcanist is reached: by its address, or by its P2P node; and the migration code it gave.</summary>
internal sealed record ArcanistEndpoint(Uri? BaseUrl, NodeTicket? Ticket, string Code)
{
    public string Describe => BaseUrl?.ToString() ?? $"p2p:{Ticket?.Id[..12]}";
}

/// <summary>
/// HTTP to the export API of another Arcanist (<see cref="ExportApi"/>): straight to its address, or through a P2P
/// tunnel to its node (<see cref="PeerClient"/>), always with the migration code. Failures are this server's codes:
/// <c>migration_code_invalid</c>, <c>migration_source_unreachable</c>, <c>migration_source_unsupported</c>.
/// </summary>
internal sealed class ArcanistSourceClient(IHttpClientFactory clients, PeerClient peers, P2PSettings p2p)
{
    public const string HttpClientName = "arcanist-source";

    public async Task<T> GetAsync<T>(ArcanistEndpoint source, string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(source, path, null, HttpCompletionOption.ResponseContentRead, cancellationToken);
        try
        {
            return (await response.Content.ReadFromJsonAsync<T>(PeerClient.Json, cancellationToken))!;
        }
        catch (JsonException)
        {
            throw new ArcanistException.Conflict(ErrorCodes.MigrationSourceUnsupported);
        }
    }

    /// <summary>A successful response, its content not read yet when <paramref name="completion"/> says so; from byte <paramref name="from"/>.</summary>
    public async Task<HttpResponseMessage> SendAsync(
        ArcanistEndpoint source, string path, long? from, HttpCompletionOption completion, CancellationToken cancellationToken)
    {
        HttpRequestMessage Request(Uri baseUrl)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUrl, ExportApi.Prefix + path));
            request.Headers.Add(ExportApi.CodeHeader, source.Code);
            request.Headers.Add(ExportApi.ClientHeader, p2p.Current.DisplayName);
            if (from is > 0)
                request.Headers.Range = new RangeHeaderValue(from, null);
            return request;
        }

        HttpResponseMessage response;
        try
        {
            if (source.Ticket is { } ticket)
                response = await peers.SendAsync(ticket, Request, completion, cancellationToken);
            else
                response = await clients.CreateClient(HttpClientName).SendAsync(Request(source.BaseUrl!), completion, cancellationToken);
        }
        catch (ArcanistException e) when (e.Code is ErrorCodes.PeerUnreachable)
        {
            throw new ArcanistException.Conflict(ErrorCodes.MigrationSourceUnreachable);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new ArcanistException.Conflict(ErrorCodes.MigrationSourceUnreachable);
        }

        if (response.IsSuccessStatusCode)
            return response;
        using (response)
        {
            // An old Arcanist (or something else) answers 404 to the whole API; a missing file answers 404 with its code.
            var error = await PeerClient.ErrorAsync(response, cancellationToken);
            throw response.StatusCode switch
            {
                HttpStatusCode.Forbidden => new ArcanistException.Forbidden(ErrorCodes.MigrationCodeInvalid),
                HttpStatusCode.TooManyRequests => new ArcanistException.Conflict(ErrorCodes.TooManyRequests),
                HttpStatusCode.NotFound when path == "/manifest" => new ArcanistException.Conflict(ErrorCodes.MigrationSourceUnsupported),
                _ => error,
            };
        }
    }

    /// <summary>
    /// The base address of a server given as text: <c>host</c>, <c>host:port</c> or a URL (http by default, port 8484
    /// when none is said), without its path.
    /// </summary>
    public static Uri? ParseUrl(string? text)
    {
        var value = text?.Trim() ?? "";
        if (value.Length is 0 or > 500)
            return null;
        if (!value.Contains("://", StringComparison.Ordinal))
            value = "http://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return null;
        var port = uri.IsDefaultPort && !text!.Contains(":" + uri.Port, StringComparison.Ordinal) && uri.Scheme == "http" ? 8484 : uri.Port;
        return new UriBuilder(uri.Scheme, uri.Host, port).Uri;
    }

    public static readonly string UserAgent = $"Arcanist/{ArcanistVersion.Current} (migration)";
}
