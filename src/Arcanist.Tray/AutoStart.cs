using System.Security;
using Microsoft.Win32;

namespace Arcanist.Tray;

/// <summary>
/// Starts the app when the user signs in: the per-user Run key on Windows (no elevation needed), a LaunchAgent in
/// <c>~/Library/LaunchAgents</c> on macOS.
/// </summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Arcanist";
    private const string AgentLabel = "net.linkfast.arcanist";

    /// <summary>
    /// Velopack keeps the executable at the same path across updates (it replaces the <c>current</c> folder on
    /// Windows, the bundle on macOS), so the registered command stays valid.
    /// </summary>
    private static string Command => $"\"{Environment.ProcessPath}\"";

    private static string AgentFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", AgentLabel + ".plist");

    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public static bool IsEnabled
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string;
            }

            return OperatingSystem.IsMacOS() && File.Exists(AgentFile);
        }
    }

    public static void Set(bool enabled)
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
                key.SetValue(ValueName, Command);
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        else if (OperatingSystem.IsMacOS())
        {
            if (!enabled)
            {
                File.Delete(AgentFile);
                return;
            }

            // launchd reads it at the next sign-in; the app is already running now. It does not keep the app alive:
            // quitting it from the menu bar stays quit until then.
            Directory.CreateDirectory(Path.GetDirectoryName(AgentFile)!);
            File.WriteAllText(AgentFile, $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                  <key>Label</key>
                  <string>{AgentLabel}</string>
                  <key>ProgramArguments</key>
                  <array>
                    <string>{SecurityElement.Escape(Environment.ProcessPath)}</string>
                  </array>
                  <key>RunAtLoad</key>
                  <true/>
                  <key>LimitLoadToSessionType</key>
                  <string>Aqua</string>
                  <key>ProcessType</key>
                  <string>Interactive</string>
                </dict>
                </plist>

                """);
        }
    }
}
