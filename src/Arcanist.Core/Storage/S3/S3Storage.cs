using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Arcanist.Core.Capabilities;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Scanning;
using Microsoft.AspNetCore.DataProtection;

namespace Arcanist.Core.Storage.S3;

/// <summary>
/// The settings of an S3 source (<see cref="LibrarySource.Settings"/>), as JSON. The secret key is protected with
/// the server's Data Protection keys and never leaves it.
/// </summary>
/// <param name="ProtectedEncryptionKey">With it, the library is encrypted on this side (<see cref="S3Encryption"/>) with
/// the owner's key, protected like the secret; null for a plain one.</param>
/// <param name="Managed">Kept by a managed extension (a hosted service's storage): it works without Ultimate Cloud's plan.</param>
internal sealed record S3SourceSettings(string Endpoint, string Region, string AccessKeyId, string ProtectedSecret, bool PathStyle,
    string? ProtectedEncryptionKey = null, bool Managed = false);

/// <summary>An open S3 source: its client, its prefix and, when it is encrypted, its keys.</summary>
internal sealed record S3Source(S3Client Client, string Prefix, S3Encryption? Encryption)
{
    public void Deconstruct(out S3Client client, out string prefix) => (client, prefix) = (Client, Prefix);
}

/// <summary>
/// Libraries in an S3 bucket (any S3-compatible service), under a prefix: the location is <c>s3://bucket/prefix</c>.
/// Reads only what it needs (<see cref="S3ReadStream"/>), and writes like local folders do: nothing is overwritten,
/// moves are a copy and a delete, and what is removed goes to <c>.arcanist-trash/</c> under the prefix. Hidden keys
/// and NAS system folders are skipped, as on disk. Needs the Cloud plan (<see cref="Capability.ThirdPartyStorage"/>):
/// without it the source is unavailable, and nothing is marked missing.
/// </summary>
internal sealed class S3Storage(
    IHttpClientFactory clients,
    IDataProtectionProvider protection,
    ICapabilityService capabilities,
    TimeProvider time,
    ILogger<S3Storage> logger) : ILibraryStorage
{
    public const string HttpClientName = "s3";
    public const string Scheme = "s3://";

    private static readonly HashSet<string> IgnoredFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "@eaDir", "#recycle", "#snapshot", "$RECYCLE.BIN", "System Volume Information", "lost+found",
    };

    private IDataProtector Protector => protection.CreateProtector("Arcanist.S3.Secret");

    public LibrarySourceKind Kind => LibrarySourceKind.S3;

    public StringComparer PathComparer => StringComparer.Ordinal;

    /// <summary><c>s3://bucket/prefix</c>, without slashes at the ends of the prefix.</summary>
    public string NormalizeLocation(string location)
    {
        var (bucket, prefix) = Split(location);
        return prefix.Length == 0 ? Scheme + bucket : $"{Scheme}{bucket}/{prefix}";
    }

    internal static (string Bucket, string Prefix) Split(string location)
    {
        var text = location.Trim();
        if (text.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
            text = text[Scheme.Length..];
        var slash = text.IndexOf('/');
        return slash < 0 ? (text, "") : (text[..slash], text[(slash + 1)..].Trim('/'));
    }

    public string Protect(string secret) => Protector.Protect(secret);

    /// <summary>A client for the source; null without the Cloud plan or with unreadable settings.</summary>
    internal S3Source? Open(StorageLocation location)
    {
        if (location.Settings is null)
            return null;
        try
        {
            var settings = JsonSerializer.Deserialize<S3SourceSettings>(location.Settings, JsonSerializerOptions.Web)!;
            if (!settings.Managed && !capabilities.IsEnabled(Capability.ThirdPartyStorage))
                return null;
            var (bucket, prefix) = Split(location.Location);
            var encryption = settings.ProtectedEncryptionKey is { } key
                ? new S3Encryption(S3Encryption.ParseKey(Protector.Unprotect(key)) ?? throw new CryptographicException("Not a 256-bit key."))
                : null;
            return new S3Source(Client(new S3Connection(new Uri(settings.Endpoint), settings.Region, bucket, settings.AccessKeyId,
                Protector.Unprotect(settings.ProtectedSecret), settings.PathStyle)), prefix, encryption);
        }
        catch (Exception e) when (e is JsonException or CryptographicException or UriFormatException or ArgumentException)
        {
            logger.LogWarning("The settings of the S3 source {Location} cannot be read: {Error}", location.Location, e.Message);
            return null;
        }
    }

    internal S3Client Client(S3Connection connection) => new(clients.CreateClient(HttpClientName), connection, time);

    private S3Source Required(StorageLocation location) =>
        Open(location) ?? throw new IOException("The S3 source is not available.");

    /// <summary>S3 keys are at most 1024 bytes; encrypted names are longer than plain ones.</summary>
    private const int MaxKeyBytes = 1024;

    private static string Key(S3Source source, string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath) || relativePath.StartsWith('/') || relativePath.Split('/').Any(s => s is "" or "." or ".."))
            throw new FileNotFoundException("The path is outside the location.", relativePath);
        var name = source.Encryption?.EncryptPath(relativePath) ?? relativePath;
        var key = source.Prefix.Length == 0 ? name : $"{source.Prefix}/{name}";
        if (System.Text.Encoding.UTF8.GetByteCount(key) > MaxKeyBytes)
            throw new IOException($"The path is too long for this library: {relativePath}");
        return key;
    }

    /// <summary>The object under an encrypted location's prefix that says which key encrypts it.</summary>
    internal const string KeyMarker = ".arcanist-key";

    /// <summary>
    /// Whether <paramref name="encryption"/> is the key of the location at <paramref name="prefix"/>: its marker says
    /// so, or there is none yet and it is written now (when the credentials can). False means another key encrypted
    /// what is there: using this one would hide that music and mix two keys under one prefix.
    /// </summary>
    internal static async Task<bool> KeyMatchesAsync(S3Client client, string prefix, S3Encryption encryption, CancellationToken cancellationToken)
    {
        var key = prefix.Length == 0 ? KeyMarker : $"{prefix}/{KeyMarker}";
        if (await client.HeadAsync(key, cancellationToken) is not null)
        {
            using var response = await client.GetAsync(key, 0, null, cancellationToken);
            return (await response.Content.ReadAsStringAsync(cancellationToken)).Trim() == encryption.KeyCheck;
        }

        try
        {
            var text = System.Text.Encoding.UTF8.GetBytes(encryption.KeyCheck + "\n");
            await client.PutAsync(key, new MemoryStream(text), text.Length, "text/plain", cancellationToken);
        }
        catch (S3Exception e) when (e.Status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            // Read-only credentials: the next writable session marks it.
        }

        return true;
    }

    public async Task<StorageProbe> ProbeAsync(StorageLocation location, CancellationToken cancellationToken)
    {
        if (Open(location) is not { } opened)
            return new StorageProbe(StorageProblem.NotFound);
        var (client, prefix) = opened;
        try
        {
            await client.ListAsync(prefix.Length == 0 ? "" : prefix + "/", null, 1, cancellationToken);
            if (opened.Encryption is { } encryption && !await KeyMatchesAsync(client, prefix, encryption, cancellationToken))
            {
                logger.LogWarning("The S3 source {Location} is encrypted with another key: it is left unavailable", location.Location);
                return new StorageProbe(StorageProblem.AccessDenied);
            }

            return StorageProbe.Available;
        }
        catch (S3Exception e) when (e.Status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            return new StorageProbe(StorageProblem.AccessDenied);
        }
        catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("The S3 source {Location} does not answer: {Error}", location.Location, e.Message);
            return new StorageProbe(StorageProblem.NotFound);
        }
    }

    public async IAsyncEnumerable<StorageEntry> EnumerateAsync(StorageLocation location, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var source = Required(location);
        var (client, prefix) = source;
        var listed = prefix.Length == 0 ? "" : prefix + "/";
        string? token = null;
        do
        {
            var page = await client.ListAsync(listed, token, 1000, cancellationToken);
            foreach (var item in page.Objects)
            {
                var relative = item.Key[listed.Length..];
                var size = item.Size;
                if (source.Encryption is { } encryption)
                {
                    // Objects that are not this library's (another key, a test of writing) are left alone.
                    if (relative.EndsWith('/') || encryption.DecryptPath(relative) is not { } plain || S3Encryption.PlainLength(size) is not { } plainSize)
                        continue;
                    (relative, size) = (plain, plainSize);
                }

                // Folder markers, hidden files (Arcanist's trash and partial uploads among them) and NAS folders.
                if (relative.Length == 0 || relative.EndsWith('/') || relative.Split('/').Any(s => s.StartsWith('.') || IgnoredFolders.Contains(s)))
                    continue;
                yield return new StorageEntry(relative, size, item.LastModified);
            }

            token = page.ContinuationToken;
        } while (token is not null);
    }

    public async Task<Stream> OpenReadAsync(StorageLocation location, string relativePath, CancellationToken cancellationToken)
    {
        if (Open(location) is not { } opened)
            throw new FileNotFoundException("The S3 source is not available.", relativePath);
        var client = opened.Client;
        var key = Key(opened, relativePath);
        var head = await client.HeadAsync(key, cancellationToken) ?? throw new FileNotFoundException("No such object.", relativePath);
        if (opened.Encryption is not { } encryption)
            return new S3ReadStream(client, key, head.Size);

        var fileKey = encryption.OpenFile(head.Metadata?.GetValueOrDefault(S3Encryption.MetadataHeader))
                      ?? throw new IOException($"{relativePath} is not encrypted with this library's key.");
        var plainLength = S3Encryption.PlainLength(head.Size) ?? throw new IOException($"{relativePath} is not a complete encrypted file.");
        return new S3DecryptingStream(new S3ReadStream(client, key, head.Size), fileKey, plainLength);
    }

    public string? TryGetLocalPath(StorageLocation location, string relativePath) => null;

    public bool Contains(string outer, string inner)
    {
        var (outerBucket, outerPrefix) = Split(outer);
        var (innerBucket, innerPrefix) = Split(inner);
        return outerBucket == innerBucket && (outerPrefix.Length == 0 || innerPrefix == outerPrefix || innerPrefix.StartsWith(outerPrefix + "/", StringComparison.Ordinal));
    }

    public async Task<bool> CanWriteAsync(StorageLocation location, CancellationToken cancellationToken)
    {
        if (Open(location) is not { } opened)
            return false;
        var client = opened.Client;
        // Plain even in an encrypted library: listing skips what it cannot decrypt.
        var test = $".arcanist-write-test-{Guid.NewGuid():N}";
        var key = opened.Prefix.Length == 0 ? test : $"{opened.Prefix}/{test}";
        try
        {
            await client.PutAsync(key, new MemoryStream([]), 0, "application/octet-stream", cancellationToken);
            await client.DeleteAsync(key, cancellationToken);
            return true;
        }
        catch (Exception e) when (e is IOException or HttpRequestException)
        {
            return false;
        }
    }

    public async Task<bool> ExistsAsync(StorageLocation location, string relativePath, CancellationToken cancellationToken)
    {
        var source = Required(location);
        var client = source.Client;
        var key = Key(source, relativePath);
        // A "folder" exists when objects are under it (encrypted names keep the folders, so this works for both).
        return await client.HeadAsync(key, cancellationToken) is not null
               || (await client.ListAsync(key + "/", null, 1, cancellationToken)).Objects.Count > 0;
    }

    public async Task MoveAsync(StorageLocation location, string from, string to, CancellationToken cancellationToken)
    {
        var opened = Required(location);
        var client = opened.Client;
        if (await ExistsAsync(location, to, cancellationToken))
            throw new StorageConflictException(to);
        var source = Key(opened, from);
        if (await client.HeadAsync(source, cancellationToken) is null)
            throw new FileNotFoundException("No such object.", from);
        // An encrypted file does not depend on its name: the copy (with its metadata) stays readable.
        await client.CopyAsync(source, Key(opened, to), cancellationToken);
        await client.DeleteAsync(source, cancellationToken);
    }

    /// <summary>Uploads a local file (an upload only shows once complete) and removes the local file, as a move would.</summary>
    public async Task ImportAsync(StorageLocation location, string relativePath, string localPath, CancellationToken cancellationToken)
    {
        var source = Required(location);
        if (await ExistsAsync(location, relativePath, cancellationToken))
            throw new StorageConflictException(relativePath);
        var key = Key(source, relativePath);
        if (source.Encryption is not { } encryption)
        {
            await using (var file = File.OpenRead(localPath))
                await source.Client.PutAsync(key, file, file.Length, ContentType(relativePath), cancellationToken);
        }
        else
        {
            // Encrypted to a temporary file first: S3 wants the length, and the upload can be retried from it.
            var (fileKey, metadata) = encryption.NewFile();
            var sealedPath = localPath + ".arcanist-sealed";
            try
            {
                await using (var plain = File.OpenRead(localPath))
                await using (var sealedFile = File.Create(sealedPath))
                    await S3Encryption.EncryptAsync(fileKey, plain, plain.Length, sealedFile, cancellationToken);
                await using var upload = File.OpenRead(sealedPath);
                // Its type would tell what it is: every encrypted object is just bytes.
                await source.Client.PutAsync(key, upload, upload.Length, "application/octet-stream", cancellationToken,
                    new Dictionary<string, string> { [S3Encryption.MetadataHeader] = metadata });
            }
            finally
            {
                File.Delete(sealedPath);
            }
        }

        File.Delete(localPath);
    }

    public async Task<string> TrashAsync(StorageLocation location, string relativePath, CancellationToken cancellationToken)
    {
        // One "folder" per day, as on disk.
        var day = $"{ILibraryStorage.TrashFolder}/{time.GetUtcNow():yyyy-MM-dd}";
        var name = AudioFormats.WithoutExtension(relativePath);
        var extension = relativePath[name.Length..];
        var target = $"{day}/{relativePath}";
        for (var copy = 2; await ExistsAsync(location, target, cancellationToken); copy++)
            target = $"{day}/{name} ({copy}){extension}";
        await MoveAsync(location, relativePath, target, cancellationToken);
        return target;
    }

    public async Task PurgeTrashAsync(StorageLocation location, DateTime before, CancellationToken cancellationToken)
    {
        if (Open(location) is not { } opened)
            return;
        var client = opened.Client;
        var trash = Key(opened, ILibraryStorage.TrashFolder) + "/";
        string? token = null;
        do
        {
            var page = await client.ListAsync(trash, token, 1000, cancellationToken);
            foreach (var item in page.Objects)
            {
                var folder = item.Key[trash.Length..].Split('/')[0];
                var day = opened.Encryption is { } encryption ? encryption.DecryptPath(folder) ?? "" : folder;
                if (DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date) && date.AddDays(1) <= before)
                    await client.DeleteAsync(item.Key, cancellationToken);
            }

            token = page.ContinuationToken;
        } while (token is not null);
    }

    /// <summary>S3 has no folders: one is gone when its last object is.</summary>
    public Task RemoveEmptyFoldersAsync(StorageLocation location, string relativeFolder, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Buckets cannot be watched: scheduled scans find changes.</summary>
    public IDisposable? Watch(StorageLocation location, Action changed) => null;

    private static string ContentType(string path) => AudioFormats.MimeType(path) is var mime && mime != "application/octet-stream"
        ? mime
        : Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".lrc" or ".cue" or ".txt" or ".log" => "text/plain; charset=utf-8",
            _ => "application/octet-stream",
        };
}
