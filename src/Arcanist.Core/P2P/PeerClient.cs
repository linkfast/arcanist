using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Application;

namespace Arcanist.Core.P2P;

/// <summary>
/// HTTP to other Arcanist servers through the P2P network: each request goes to the local port of a tunnel to the
/// server's node. When the tunnel no longer answers (the node restarted, its addresses changed), it is opened again
/// once.
/// </summary>
public sealed class PeerClient(P2PNode node, IHttpClientFactory clients)
{
    public const string HttpClientName = "p2p";

    private HttpClient Client => clients.CreateClient(HttpClientName);

    /// <summary>Sends a request built for the tunnel's base address (<c>http://127.0.0.1:port</c>).</summary>
    /// <exception cref="ArcanistException"><c>p2p_disabled</c> or <c>peer_unreachable</c>.</exception>
    public async Task<HttpResponseMessage> SendAsync(
        NodeTicket ticket, Func<Uri, HttpRequestMessage> request, HttpCompletionOption completion, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            int port;
            try
            {
                port = await node.TunnelAsync(ticket, cancellationToken);
            }
            catch (P2PException e)
            {
                throw new ArcanistException.Conflict(e.Code == "p2p_disabled" ? ErrorCodes.P2PDisabled : ErrorCodes.PeerUnreachable);
            }

            try
            {
                return await Client.SendAsync(request(new Uri($"http://127.0.0.1:{port}")), completion, cancellationToken);
            }
            catch (HttpRequestException) when (attempt == 0)
            {
                node.CloseTunnel(ticket.Id);
            }
            catch (HttpRequestException)
            {
                throw new ArcanistException.Conflict(ErrorCodes.PeerUnreachable);
            }
        }
    }

    public async Task<T> GetAsync<T>(NodeTicket ticket, string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(ticket, b => new HttpRequestMessage(HttpMethod.Get, new Uri(b, path)), HttpCompletionOption.ResponseContentRead, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    public async Task<T> PostAsync<T>(NodeTicket ticket, string path, object body, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(ticket, b => new HttpRequestMessage(HttpMethod.Post, new Uri(b, path)) { Content = JsonContent.Create(body, options: Json) },
            HttpCompletionOption.ResponseContentRead, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The body, or the other server's error code as an <see cref="ArcanistException"/>.</summary>
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken))!;
        throw await ErrorAsync(response, cancellationToken);
    }

    internal static async Task<ArcanistException> ErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string? code = null;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json, cancellationToken);
            code = problem.TryGetProperty("code", out var value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Forbidden => new ArcanistException.Forbidden(code ?? ErrorCodes.Forbidden),
            HttpStatusCode.NotFound => new ArcanistException.NotFound(),
            _ => new ArcanistException.Conflict(code ?? ErrorCodes.PeerUnreachable),
        };
    }
}
