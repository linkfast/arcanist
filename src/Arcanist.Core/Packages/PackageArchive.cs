using System.IO.Compression;
using System.Text.Json;
using Arcanist.Core.Application;

namespace Arcanist.Core.Packages;

/// <summary>
/// Unpacks the .zip of a frontend or extension safely: no entry may land outside the destination, sizes are
/// bounded, and the package may sit at the root of the archive or inside a single top-level folder (as GitHub
/// release archives do).
/// </summary>
public static class PackageArchive
{
    /// <summary>Largest archive accepted, compressed.</summary>
    public const long MaxArchiveBytes = 250L * 1024 * 1024;

    private const long MaxUnpackedBytes = 1024L * 1024 * 1024;
    private const int MaxEntries = 20_000;

    /// <summary>
    /// Extracts <paramref name="archive"/> into a new folder under <paramref name="staging"/> and returns the folder
    /// holding <paramref name="manifestName"/>. Throws a validation error (field <c>file</c>) when the archive is not
    /// a valid package.
    /// </summary>
    public static async Task<string> ExtractAsync(Stream archive, string staging, string manifestName, CancellationToken cancellationToken)
    {
        var destination = Path.Combine(staging, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;

        try
        {
            using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count > MaxEntries)
                throw Invalid();

            long unpacked = 0;
            foreach (var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Folders are implied by the files in them.
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                    continue;

                var target = Path.GetFullPath(Path.Combine(destination, entry.FullName.Replace('\\', '/')));
                if (!target.StartsWith(root, StringComparison.Ordinal))
                    throw Invalid();

                unpacked += entry.Length;
                if (unpacked > MaxUnpackedBytes)
                    throw Invalid();

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = entry.Open();
                await using var output = File.Create(target);
                await input.CopyToAsync(output, cancellationToken);
            }
        }
        // What ZipArchive throws for files that are not (complete) zip archives.
        catch (Exception e) when (e is InvalidDataException or ArgumentOutOfRangeException or EndOfStreamException)
        {
            Directory.Delete(destination, recursive: true);
            throw Invalid();
        }
        catch
        {
            Directory.Delete(destination, recursive: true);
            throw;
        }

        if (File.Exists(Path.Combine(destination, manifestName)))
            return destination;

        var folders = Directory.GetDirectories(destination);
        if (folders.Length == 1 && Directory.GetFiles(destination).Length == 0 && File.Exists(Path.Combine(folders[0], manifestName)))
            return folders[0];

        Directory.Delete(destination, recursive: true);
        throw ArcanistException.Field("file", ErrorCodes.ManifestMissing);
    }

    /// <summary>Reads a package manifest, or fails with <c>manifest_invalid</c>.</summary>
    public static T ReadManifest<T>(string folder, string manifestName)
    {
        try
        {
            var json = File.ReadAllText(Path.Combine(folder, manifestName));
            return JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web) ?? throw ArcanistException.Field("file", ErrorCodes.ManifestInvalid);
        }
        catch (JsonException)
        {
            throw ArcanistException.Field("file", ErrorCodes.ManifestInvalid);
        }
    }

    /// <summary>Package ids: lowercase letters, digits and dashes, 2–64 characters, starting with a letter or digit.</summary>
    public static bool IsValidId(string? id) =>
        id is { Length: >= 2 and <= 64 } && char.IsAsciiLetterOrDigit(id[0])
        && id.All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterLower(c) || c == '-');

    /// <summary>Downloads an archive from an http(s) URL, refusing anything larger than <see cref="MaxArchiveBytes"/>.</summary>
    public static async Task<Stream> DownloadAsync(HttpClient http, string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw ArcanistException.Field("url", ErrorCodes.InvalidFormat);

        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxArchiveBytes)
                throw ArcanistException.Field("url", ErrorCodes.PackageTooLarge);

            var buffer = new MemoryStream();
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            var chunk = new byte[81920];
            int read;
            while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaxArchiveBytes)
                    throw ArcanistException.Field("url", ErrorCodes.PackageTooLarge);
                buffer.Write(chunk, 0, read);
            }

            buffer.Position = 0;
            return buffer;
        }
        catch (HttpRequestException)
        {
            throw ArcanistException.Field("url", ErrorCodes.DownloadFailed);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw ArcanistException.Field("url", ErrorCodes.DownloadFailed);
        }
    }

    private static ArcanistException Invalid() => ArcanistException.Field("file", ErrorCodes.PackageInvalid);
}
