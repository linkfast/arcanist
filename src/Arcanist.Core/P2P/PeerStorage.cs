using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Arcanist.Core.Application;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.P2P;

/// <summary>
/// The libraries another server shares (<see cref="LibrarySourceKind.Peer"/>; the location is the
/// <see cref="P2PRemote"/> id). Its files are described by that server, never listed or read here for scanning;
/// playing them goes through <see cref="PeerStreaming"/>. Nothing can be written.
/// </summary>
internal sealed class PeerStorage(IArcanistDbContextFactory databases, PeerClient client) : ILibraryStorage, IDescribedStorage
{
    public LibrarySourceKind Kind => LibrarySourceKind.Peer;

    public string NormalizeLocation(string location) => location.Trim();

    /// <summary>The remote's ticket, or null when it is gone or revoked.</summary>
    internal async Task<(P2PRemote Remote, NodeTicket Ticket)?> RemoteAsync(StorageLocation location, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(location.Location, out var id))
            return null;
        await using var db = databases.CreateDbContext();
        var remote = await db.P2PRemotes.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (remote is not { Status: P2PRemoteStatus.Active })
            return null;
        return (remote, JsonSerializer.Deserialize<NodeTicket>(remote.Ticket, NativeP2P.Json)!);
    }

    public async Task<StorageProbe> ProbeAsync(StorageLocation location, CancellationToken cancellationToken)
    {
        if (await RemoteAsync(location, cancellationToken) is not { } remote)
            return new StorageProbe(StorageProblem.NotFound);
        try
        {
            await client.GetAsync<PeerShareInfo>(remote.Ticket, "/rest/v1/peer/info", cancellationToken);
            return StorageProbe.Available;
        }
        catch (ArcanistException e) when (e is ArcanistException.Forbidden)
        {
            // Revoked or expired on the other side.
            await MarkRevokedAsync(remote.Remote.Id, cancellationToken);
            return new StorageProbe(StorageProblem.AccessDenied);
        }
        catch (ArcanistException)
        {
            return new StorageProbe(StorageProblem.NotFound);
        }
    }

    public async IAsyncEnumerable<DescribedFile> DescribeAsync(StorageLocation location, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var remote = await RemoteAsync(location, cancellationToken) ?? throw new IOException("The share is gone.");
        Guid? cursor = null;
        do
        {
            PeerFilePage page;
            try
            {
                page = await client.GetAsync<PeerFilePage>(remote.Ticket,
                    cursor is null ? "/rest/v1/peer/files" : $"/rest/v1/peer/files?after={cursor}", cancellationToken);
            }
            catch (ArcanistException e)
            {
                // The scanner treats it as a source that is not available, and marks nothing missing.
                throw new IOException(e.Code, e);
            }

            foreach (var file in page.Items)
            {
                yield return new DescribedFile(file.Path, file.CueTrack, file.StartMs, file.EndMs, file.Size, file.ModifiedAt, file.ContentHash,
                    file.Artwork, file.Metadata);
            }

            cursor = page.NextCursor;
        } while (cursor is not null);

        await TouchAsync(remote.Remote.Id, cancellationToken);
    }

    public async Task<byte[]?> ReadArtworkAsync(StorageLocation location, string artwork, CancellationToken cancellationToken)
    {
        if (await RemoteAsync(location, cancellationToken) is not { } remote)
            return null;
        using var response = await client.SendAsync(remote.Ticket,
            b => new HttpRequestMessage(HttpMethod.Get, new Uri(b, $"/rest/v1/peer/artwork/{Uri.EscapeDataString(artwork)}")),
            HttpCompletionOption.ResponseContentRead, cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(cancellationToken) : null;
    }

    public async IAsyncEnumerable<StorageEntry> EnumerateAsync(StorageLocation location, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? last = null;
        await foreach (var file in DescribeAsync(location, cancellationToken))
        {
            if (file.RelativePath != last)
                yield return new StorageEntry(file.RelativePath, file.Size, file.ModifiedAt);
            last = file.RelativePath;
        }
    }

    /// <summary>The original file, when the other server allows downloads (for copies). Not seekable.</summary>
    public async Task<Stream> OpenReadAsync(StorageLocation location, string relativePath, CancellationToken cancellationToken)
    {
        var remote = await RemoteAsync(location, cancellationToken) ?? throw new FileNotFoundException(relativePath);
        var response = await client.SendAsync(remote.Ticket,
            b => new HttpRequestMessage(HttpMethod.Get, new Uri(b, $"/rest/v1/peer/stream?path={Uri.EscapeDataString(relativePath)}&download=true")),
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadAsStreamAsync(cancellationToken);
        response.Dispose();
        throw response.StatusCode == HttpStatusCode.NotFound ? new FileNotFoundException(relativePath) : new IOException(ErrorCodes.DownloadNotAllowed);
    }

    /// <summary>
    /// The audio as the other server streams it (its original unless its rules cap the bit rate): what listening
    /// takes, without its permission to download. For analyses, which only need to hear it.
    /// </summary>
    public async Task<Stream> OpenStreamAsync(StorageLocation location, string relativePath, CancellationToken cancellationToken)
    {
        var remote = await RemoteAsync(location, cancellationToken) ?? throw new FileNotFoundException(relativePath);
        var response = await client.SendAsync(remote.Ticket,
            b => new HttpRequestMessage(HttpMethod.Get, new Uri(b, $"/rest/v1/peer/stream?path={Uri.EscapeDataString(relativePath)}")),
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadAsStreamAsync(cancellationToken);
        response.Dispose();
        throw response.StatusCode == HttpStatusCode.NotFound ? new FileNotFoundException(relativePath) : new IOException($"HTTP {(int)response.StatusCode}");
    }

    public string? TryGetLocalPath(StorageLocation location, string relativePath) => null;

    public bool Contains(string outer, string inner) => string.Equals(outer, inner, StringComparison.Ordinal);

    public StringComparer PathComparer => StringComparer.Ordinal;

    public Task<bool> CanWriteAsync(StorageLocation location, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<bool> ExistsAsync(StorageLocation location, string relativePath, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task MoveAsync(StorageLocation location, string from, string to, CancellationToken cancellationToken) => throw ReadOnly();

    public Task ImportAsync(StorageLocation location, string relativePath, string localPath, CancellationToken cancellationToken) => throw ReadOnly();

    public Task<string> TrashAsync(StorageLocation location, string relativePath, CancellationToken cancellationToken) => throw ReadOnly();

    public Task PurgeTrashAsync(StorageLocation location, DateTime before, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RemoveEmptyFoldersAsync(StorageLocation location, string relativeFolder, CancellationToken cancellationToken) => Task.CompletedTask;

    public IDisposable? Watch(StorageLocation location, Action changed) => null;

    private static NotSupportedException ReadOnly() => new("Shared libraries are read-only.");

    private async Task TouchAsync(Guid remoteId, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        await db.P2PRemotes.Where(r => r.Id == remoteId).ExecuteUpdateAsync(s => s.SetProperty(r => r.LastContactAt, DateTime.UtcNow), cancellationToken);
    }

    private async Task MarkRevokedAsync(Guid remoteId, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        await db.P2PRemotes.Where(r => r.Id == remoteId).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, P2PRemoteStatus.Revoked), cancellationToken);
    }
}
