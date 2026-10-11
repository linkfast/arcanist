using Microsoft.Win32;

namespace Arcanist.Tray;

/// <summary>Starts the app when the user signs in, through the per-user Run key (no elevation needed).</summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Arcanist";

    /// <summary>
    /// Velopack keeps the executable at the same path across updates (it replaces the <c>current</c> folder), so the
    /// registered command stays valid.
    /// </summary>
    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, Command);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
