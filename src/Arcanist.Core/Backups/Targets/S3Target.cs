using System.Net;
using System.Runtime.CompilerServices;
using Arcanist.Core.Application;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Storage;
using Arcanist.Core.Storage.S3;

namespace Arcanist.Core.Backups.Targets;

/// <summary>Opens S3 destinations with the S3 libraries' client and settings (the Cloud plan).</summary>
internal sealed class S3BackupTargets(S3Storage storage)
{
    public IBackupTarget Open(BackupDestination destination) =>
        storage.Open(new StorageLocation(LibrarySourceKind.S3, destination.Location, destination.Settings)) is { } opened
            ? new S3Target(opened.Client, opened.Prefix)
            : new UnavailableTarget(ErrorCodes.S3Unavailable);
}

/// <summary>A prefix of an S3 bucket. Uploads only show once complete, so nothing is half-written.</summary>
internal sealed class S3Target(S3Client client, string prefix) : IBackupTarget
{
    /// <summary>The largest object a single PUT can write.</summary>
    private const long MaxObjectSize = 5L * 1024 * 1024 * 1024;

    private string Key(string path)
    {
        if (!BackupPaths.IsSafe(path))
            throw new ArgumentException("The path is not inside the destination.", nameof(path));
        return prefix.Length == 0 ? path : $"{prefix}/{path}";
    }

    private string Relative(string key) => prefix.Length == 0 ? key : key[(prefix.Length + 1)..];

    public async Task<string?> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await client.ListAsync(prefix.Length == 0 ? "" : prefix + "/", null, 1, cancellationToken);
            return null;
        }
        catch (S3Exception e) when (e.Status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
                                    || e.Code is "AccessDenied" or "SignatureDoesNotMatch" or "InvalidAccessKeyId")
        {
            return ErrorCodes.S3AccessDenied;
        }
        catch (S3Exception e) when (e.Status == HttpStatusCode.NotFound || e.Code == "NoSuchBucket")
        {
            return ErrorCodes.S3BucketNotFound;
        }
        catch (Exception e) when (e is IOException or HttpRequestException || e is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return ErrorCodes.S3Unreachable;
        }
    }

    public async Task<BackupObject?> StatAsync(string path, CancellationToken cancellationToken) =>
        await client.HeadAsync(Key(path), cancellationToken) is { } head ? new BackupObject(path, head.Size) : null;

    public async Task WriteAsync(string path, Stream content, long length, CancellationToken cancellationToken)
    {
        if (length > MaxObjectSize)
            throw new ArcanistException.Conflict(ErrorCodes.BackupFileTooLarge);
        var key = Key(path);
        if (await client.HeadAsync(key, cancellationToken) is not null)
            throw new StorageConflictException(path);
        await client.PutAsync(key, content, length, "application/octet-stream", cancellationToken);
    }

    public async Task MoveAsync(string from, string to, CancellationToken cancellationToken)
    {
        var target = Key(to);
        if (await client.HeadAsync(target, cancellationToken) is not null)
            throw new StorageConflictException(to);
        var source = Key(from);
        if (await client.HeadAsync(source, cancellationToken) is null)
            throw new FileNotFoundException("No such object.", from);
        await client.CopyAsync(source, target, cancellationToken);
        await client.DeleteAsync(source, cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        var key = Key(path);
        var head = await client.HeadAsync(key, cancellationToken) ?? throw new FileNotFoundException("No such object.", path);
        return new S3ReadStream(client, key, head.Size);
    }

    public async IAsyncEnumerable<BackupObject> ListAsync(string folder, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var listed = folder.Length == 0 ? (prefix.Length == 0 ? "" : prefix + "/") : Key(folder) + "/";
        string? token = null;
        do
        {
            var page = await client.ListAsync(listed, token, 1000, cancellationToken);
            foreach (var item in page.Objects)
            {
                if (!item.Key.EndsWith('/'))
                    yield return new BackupObject(Relative(item.Key), item.Size);
            }

            token = page.ContinuationToken;
        } while (token is not null);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken) => client.DeleteAsync(Key(path), cancellationToken);

    public async Task DeleteFolderAsync(string folder, CancellationToken cancellationToken)
    {
        var keys = new List<string>();
        await foreach (var item in ListAsync(folder, cancellationToken))
            keys.Add(item.Path);
        foreach (var key in keys)
            await client.DeleteAsync(Key(key), cancellationToken);
    }
}

/// <summary>A destination that cannot be used now (the Cloud plan is gone, the other server stopped keeping backups…).</summary>
internal sealed class UnavailableTarget(string code) : IBackupTarget
{
    public Task<string?> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(code);

    private Exception Unavailable() => new ArcanistException.Conflict(code);

    public Task<BackupObject?> StatAsync(string path, CancellationToken cancellationToken) => throw Unavailable();

    public Task WriteAsync(string path, Stream content, long length, CancellationToken cancellationToken) => throw Unavailable();

    public Task MoveAsync(string from, string to, CancellationToken cancellationToken) => throw Unavailable();

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken) => throw Unavailable();

    public IAsyncEnumerable<BackupObject> ListAsync(string folder, CancellationToken cancellationToken) => throw Unavailable();

    public Task DeleteAsync(string path, CancellationToken cancellationToken) => throw Unavailable();

    public Task DeleteFolderAsync(string folder, CancellationToken cancellationToken) => throw Unavailable();
}
