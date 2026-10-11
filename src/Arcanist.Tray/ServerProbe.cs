using System.Diagnostics;
using System.Text.Json;

namespace Arcanist.Tray;

/// <summary>Contents of the server's <c>runtime.json</c>.</summary>
internal sealed record RuntimeInfo(int ProcessId, string Version, IReadOnlyList<string> Addresses, DateTime StartedAt);

/// <summary>Finds a running server, whoever started it, through its <c>runtime.json</c> and <c>/health</c>.</summary>
internal sealed class ServerProbe(Installation installation) : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };

    /// <summary>The announced server, or null when there is none or its process is gone (a stale file).</summary>
    public RuntimeInfo? ReadRuntimeInfo()
    {
        try
        {
            var info = JsonSerializer.Deserialize<RuntimeInfo>(File.ReadAllText(installation.RuntimeFile), JsonSerializerOptions.Web);
            return info is not null && IsAlive(info.ProcessId) ? info : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public async Task<bool> IsHealthyAsync(Uri baseUrl)
    {
        try
        {
            using var response = await _http.GetAsync(new Uri(baseUrl, "health"));
            return response.IsSuccessStatusCode;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// The address to open on this machine: plain HTTP when available (a development certificate would only bring
    /// warnings) and <c>localhost</c> instead of a wildcard such as <c>[::]</c>.
    /// </summary>
    public static Uri? LocalUrl(IEnumerable<string> addresses)
    {
        Uri? best = null;
        foreach (var address in addresses)
        {
            // Kestrel reports wildcards as [::] or 0.0.0.0; configured URLs may also use * or +.
            var normalized = address.Replace("://*:", "://localhost:").Replace("://+:", "://localhost:");
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
                continue;

            var builder = new UriBuilder(uri) { Path = "/" };
            if (uri.Host is "[::]" or "::" or "0.0.0.0")
                builder.Host = "localhost";

            if (best is null || (uri.Scheme == Uri.UriSchemeHttp && best.Scheme != Uri.UriSchemeHttp))
                best = builder.Uri;
        }

        return best;
    }

    private static bool IsAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Win32Exception: a process of another account (the service) whose exit state cannot be read is alive.
            return e is System.ComponentModel.Win32Exception;
        }
    }

    public void Dispose() => _http.Dispose();
}
