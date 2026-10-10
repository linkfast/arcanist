namespace Arcanist.Core.Hosting;

/// <summary>
/// Layout of the data directory. Everything Arcanist writes lives under <see cref="Root"/>,
/// so backing up or moving an installation means copying a single folder.
/// </summary>
public sealed class ArcanistPaths
{
    public const string SettingsFileName = "arcanist.json";

    public ArcanistPaths(string root)
    {
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    /// <summary>Settings written by the setup wizard and the admin panel.</summary>
    public string SettingsFile => Path.Combine(Root, SettingsFileName);

    /// <summary>Token signing and Data Protection keys. Must persist across restarts and upgrades.</summary>
    public string Keys => Path.Combine(Root, "keys");

    public string Cache => Path.Combine(Root, "cache");

    public string Frontends => Path.Combine(Root, "frontends");

    public string Extensions => Path.Combine(Root, "extensions");

    public string Logs => Path.Combine(Root, "logs");

    public void EnsureCreated()
    {
        foreach (var directory in new[] { Root, Keys, Cache, Frontends, Extensions, Logs })
            Directory.CreateDirectory(directory);
    }
}
