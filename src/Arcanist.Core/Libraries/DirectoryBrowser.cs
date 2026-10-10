namespace Arcanist.Core.Libraries;

public sealed record DirectoryEntry(string Name, string Path, bool IsHidden);

public sealed record DirectoryListing(string? Path, string? Parent, IReadOnlyList<DirectoryEntry> Directories);

public enum DirectoryBrowseError
{
    NotFound,
    AccessDenied,
}

/// <summary>
/// Lists folders on the server so administrators can pick library locations from the UI. Exposes the server's
/// file system layout, so it must only be reachable during setup or by administrators.
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
