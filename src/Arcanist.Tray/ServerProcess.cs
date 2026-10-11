using System.Diagnostics;

namespace Arcanist.Tray;

/// <summary>
/// Runs the bundled server as a child process under the signed-in user (the default mode). The server stops when its
/// standard input closes, which is how <see cref="StopAsync"/> asks it to and also what happens if this app crashes.
/// </summary>
internal sealed class ServerProcess(Installation installation) : IDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(20);

    private Process? _process;
    private bool _stopping;

    /// <summary>Raised on the thread pool when the server exits without being asked to.</summary>
    public event Action<int>? ExitedUnexpectedly;

    public bool IsRunning => _process is { HasExited: false };

    public DateTime? StartedAt { get; private set; }

    public bool CanStart => File.Exists(installation.ServerExecutable);

    public void Start()
    {
        if (IsRunning)
            return;

        var start = new ProcessStartInfo(installation.ServerExecutable)
        {
            ArgumentList = { "--data-dir", installation.DataDirectory, "--Arcanist:Hosting:ExitWhenInputCloses=true" },
            WorkingDirectory = installation.ServerDirectory,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        _stopping = false;
        _process?.Dispose();
        _process = new Process { StartInfo = start, EnableRaisingEvents = true };
        _process.Exited += OnExited;
        _process.Start();
        StartedAt = DateTime.UtcNow;
    }

    public async Task StopAsync()
    {
        if (_process is not { HasExited: false } process)
            return;

        _stopping = true;
        process.StandardInput.Close();

        using var timeout = new CancellationTokenSource(StopTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    private void OnExited(object? sender, EventArgs e)
    {
        var process = (Process)sender!;
        if (!_stopping && ReferenceEquals(process, _process))
            ExitedUnexpectedly?.Invoke(process.ExitCode);
    }

    public void Dispose() => _process?.Dispose();
}
