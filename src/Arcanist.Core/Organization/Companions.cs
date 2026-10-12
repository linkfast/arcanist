using Arcanist.Core.Scanning;

namespace Arcanist.Core.Organization;

/// <summary>
/// Decides where the other files of an album folder go when its audio moves: artwork, booklets, rip logs, playlists
/// and subfolders without audio (scans) follow when all of the folder's audio goes to one new folder. Folders whose
/// audio splits up, or partly stays, keep them.
/// </summary>
internal static class Companions
{
    /// <param name="entries">Every file of the location (or drop folder), as relative paths.</param>
    /// <param name="destinations">For every audio file, the folder it ends up in (its current folder when it stays);
    /// null when it is unknown or stays for another reason.</param>
    /// <param name="claimed">Files already moved (lyrics and cue sheets that follow their audio).</param>
    public static List<(string From, string To)> Plan(
        IEnumerable<string> entries,
        IReadOnlyDictionary<string, string?> destinations,
        IReadOnlySet<string> claimed,
        StringComparer comparer)
    {
        // For every folder with audio below it: the one folder all that audio goes to, or null when it does not
        // all go to the same place.
        var folders = new Dictionary<string, string?>(comparer);
        foreach (var (file, destination) in destinations)
        {
            for (var folder = AudioFormats.Folder(file); folder.Length > 0; folder = AudioFormats.Folder(folder))
            {
                if (!folders.TryGetValue(folder, out var current))
                    folders[folder] = destination;
                else if (current is not null && (destination is null || !comparer.Equals(current, destination)))
                    folders[folder] = null;
            }
        }

        var moves = new List<(string From, string To)>();
        foreach (var entry in entries)
        {
            if (claimed.Contains(entry) || destinations.ContainsKey(entry))
                continue;

            // The album folder of a file is the nearest folder above it with audio below. The root of a location
            // holds loose files of all kinds, so nothing there follows anything.
            var folder = AudioFormats.Folder(entry);
            while (folder.Length > 0 && !folders.ContainsKey(folder))
                folder = AudioFormats.Folder(folder);

            if (folder.Length == 0 || folders[folder] is not { } destination || comparer.Equals(destination, folder))
                continue;

            var rest = entry[(folder.Length + 1)..];
            moves.Add((entry, destination.Length == 0 ? rest : $"{destination}/{rest}"));
        }

        return moves;
    }
}
