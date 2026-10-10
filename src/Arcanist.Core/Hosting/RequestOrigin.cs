using System.Net;

namespace Arcanist.Core.Hosting;

public static class RequestOrigin
{
    private static readonly string[] ProxyHeaders = ["X-Forwarded-For", "X-Forwarded-Host", "Forwarded", "X-Real-IP"];

    /// <summary>
    /// True when the request comes from this machine. Requests relayed by a reverse proxy running on the same
    /// host arrive from loopback too, so any proxy header makes the request count as remote.
    /// </summary>
    public static bool IsLocal(HttpContext context)
    {
        if (ProxyHeaders.Any(context.Request.Headers.ContainsKey))
            return false;

        // No address means a Unix domain socket or named pipe, which are only reachable locally.
        var remote = context.Connection.RemoteIpAddress;
        return remote is null || IPAddress.IsLoopback(remote);
    }
}
