namespace Arcanist.Core.Libraries;

public sealed record DirectoryEntry(string Name, string Path, bool IsHidden);

public sealed record DirectoryListing(string? Path, string? Parent, IReadOnlyList<DirectoryEntry> Directories);

public enum DirectoryBrowseError
{
    NotFound,
    AccessDenied,
    /// <summary>The server cannot create folders there (permissions, or a read-only mount).</summary>
    ReadOnly,
    /// <summary>The name is not a valid folder name on every system Arcanist runs on.</summary>
    InvalidName,
    AlreadyExists,
}

/// <summary>
/// Lists folders on the server so administrators can pick library locations from the UI, and creates new ones to
/// pick. Exposes the server's file system layout, so it must only be reachable during setup or by administrators.
/// </summary>
public sealed class DirectoryBrowser
{
    /// <summary>Lists the subfolders of <paramref name="path"/>, or the file system roots when it is empty.</summary>
    public (DirectoryListing? Listing, DirectoryBrowseError? Error) List(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return (new DirectoryListing(null, null, GetRoots()), null);

        var fullPath = System.IO.Path.GetFullPath(path);
        var directory = new DirectoryInfo(fullPath);
        if (!directory.Exists)
            return (null, DirectoryBrowseError.NotFound);

        try
        {
            var entries = directory
                .EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 })
                .Select(d => new DirectoryEntry(d.Name, d.FullName, IsHidden(d)))
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            return (new DirectoryListing(directory.FullName, directory.Parent?.FullName, entries), null);
        }
        catch (UnauthorizedAccessException)
        {
            return (null, DirectoryBrowseError.AccessDenied);
        }
    }

    /// <summary>
    /// Creates the folder <paramref name="name"/> inside <paramref name="parent"/>. Names are held to Windows rules
    /// (the strictest), so a folder made on a NAS stays usable from every computer that shares it.
    /// </summary>
    public (DirectoryEntry? Directory, DirectoryBrowseError? Error) Create(string? parent, string? name)
    {
        name = name?.Trim();
        if (!IsValidName(name))
            return (null, DirectoryBrowseError.InvalidName);
        if (string.IsNullOrWhiteSpace(parent) || !System.IO.Path.IsPathFullyQualified(parent))
            return (null, DirectoryBrowseError.NotFound);

        var parentPath = System.IO.Path.GetFullPath(parent);
        if (!Directory.Exists(parentPath))
            return (null, DirectoryBrowseError.NotFound);

        var path = System.IO.Path.Combine(parentPath, name!);
        if (Directory.Exists(path) || File.Exists(path))
            return (null, DirectoryBrowseError.AlreadyExists);

        try
        {
            var directory = Directory.CreateDirectory(path);
            return (new DirectoryEntry(directory.Name, directory.FullName, IsHidden(directory)), null);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return (null, DirectoryBrowseError.ReadOnly);
        }
    }

    private static readonly char[] InvalidNameCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    internal static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name)
        && name.Length <= 200
        && name is not ("." or "..")
        && !name.EndsWith('.')
        && name.IndexOfAny(InvalidNameCharacters) < 0
        && !name.Any(char.IsControl)
        && !ReservedNames.Contains(name.Split('.')[0].TrimEnd());

    private static List<DirectoryEntry> GetRoots()
    {
        if (!OperatingSystem.IsWindows())
            return [new DirectoryEntry("/", "/", false)];

        return DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => new DirectoryEntry(
                string.IsNullOrEmpty(d.VolumeLabel) ? d.Name : $"{d.VolumeLabel} ({d.Name.TrimEnd('\\')})",
                d.RootDirectory.FullName,
                false))
            .ToList();
    }

    private static bool IsHidden(DirectoryInfo directory) =>
        directory.Name.StartsWith('.') || directory.Attributes.HasFlag(FileAttributes.Hidden);
}
