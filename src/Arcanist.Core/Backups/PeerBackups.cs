using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Arcanist.Core.Application;
using Arcanist.Core.Backups.Targets;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.P2P;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Backups;

/// <summary>How much space another server's backups take here, and how much they may.</summary>
public sealed record PeerBackupSpace(long UsedBytes, long? QuotaBytes);

/// <summary>
/// This server keeping other servers' backups: each server peer that accepted an invitation to do so has a folder
/// here (<see cref="P2PPeer.BackupFolder"/>) and maybe a quota. Writes follow the destinations' rules: nothing is
/// overwritten, and everything stays inside the folder.
/// </summary>
internal sealed class PeerBackupStore(IArcanistDbContextFactory databases)
{
    private readonly ConcurrentDictionary<Guid, long> _used = new();

    public async Task<(FolderTarget Target, Guid PeerId, long? Quota)> OpenAsync(P2PPeerSnapshot caller, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var peer = await db.P2PPeers.AsNoTracking().Where(p => p.Id == caller.Id).Select(p => new { p.BackupFolder, p.BackupQuotaBytes })
            .SingleOrDefaultAsync(cancellationToken);
        if (peer?.BackupFolder is not { } folder)
            throw new ArcanistException.Forbidden(ErrorCodes.BackupsNotAccepted);
        return (new FolderTarget(folder), caller.Id, peer.BackupQuotaBytes);
    }

    /// <summary>What the peer's backups take, counted once and then kept up to date as they change.</summary>
    public async Task<long> UsedAsync(Guid peerId, IBackupTarget target, CancellationToken cancellationToken)
    {
        if (_used.TryGetValue(peerId, out var used))
            return used;
        used = 0;
        await foreach (var file in target.ListAsync("", cancellationToken))
            used += file.Size;
        return _used.GetOrAdd(peerId, used);
    }

    public void Add(Guid peerId, long bytes) => _used.AddOrUpdate(peerId, _ => Math.Max(0, bytes), (_, used) => Math.Max(0, used + bytes));

    /// <summary>Forgets the count (a folder deleted: recounted on next use).</summary>
    public void Forget(Guid peerId) => _used.TryRemove(peerId, out _);
}

/// <summary>Another Arcanist server that keeps this server's backups, through the P2P network.</summary>
internal sealed class PeerBackupTargets(PeerClient client)
{
    public IBackupTarget Open(BackupDestination destination)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<PeerDestinationSettings>(destination.Settings ?? "", JsonSerializerOptions.Web);
            return settings?.Ticket is { } ticket ? new PeerTarget(client, ticket) : new UnavailableTarget(ErrorCodes.BackupsNotAccepted);
        }
        catch (JsonException)
        {
            return new UnavailableTarget(ErrorCodes.BackupsNotAccepted);
        }
    }
}

/// <summary>What a peer destination keeps: the other server's ticket, to reach its node.</summary>
internal sealed record PeerDestinationSettings(NodeTicket Ticket);

/// <summary>The other server's folder for this server's backups, through <c>/rest/v1/peer/backups</c>.</summary>
internal sealed class PeerTarget(PeerClient client, NodeTicket ticket) : IBackupTarget
{
    private const string Base = "/rest/v1/peer/backups";

    private static string Query(string name, string value) => $"{name}={Uri.EscapeDataString(value)}";

    public async Task<string?> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await client.GetAsync<PeerBackupSpace>(ticket, $"{Base}/space", cancellationToken);
            return null;
        }
        catch (ArcanistException.Forbidden)
        {
            return ErrorCodes.BackupsNotAccepted;
        }
        catch (ArcanistException e)
        {
            return e.Code == ErrorCodes.NotFound ? ErrorCodes.BackupsNotAccepted : e.Code;
        }
    }

    public async Task<BackupObject?> StatAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await client.GetAsync<BackupObject>(ticket, $"{Base}/stat?{Query("path", path)}", cancellationToken);
        }
        catch (ArcanistException.NotFound)
        {
            return null;
        }
    }

    public async Task WriteAsync(string path, Stream content, long length, CancellationToken cancellationToken)
    {
        // The content can be read once: a retry of the tunnel sends nothing more, and the other side refuses it.
        var sent = false;
        using var response = await client.SendAsync(ticket, b =>
        {
            var request = new HttpRequestMessage(HttpMethod.Put, new Uri(b, $"{Base}/file?{Query("path", path)}"));
            request.Content = sent ? new ByteArrayContent([]) : new StreamContent(content);
            request.Content.Headers.ContentLength = sent ? 0 : length;
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            sent = true;
            return request;
        }, HttpCompletionOption.ResponseContentRead, cancellationToken);
        await EnsureAsync(response, path, cancellationToken);
    }

    public async Task MoveAsync(string from, string to, CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(ticket,
            b => new HttpRequestMessage(HttpMethod.Post, new Uri(b, $"{Base}/move?{Query("from", from)}&{Query("to", to)}")),
            HttpCompletionOption.ResponseContentRead, cancellationToken);
        await EnsureAsync(response, to, cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        var response = await client.SendAsync(ticket, b => new HttpRequestMessage(HttpMethod.Get, new Uri(b, $"{Base}/file?{Query("path", path)}")),
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            throw new FileNotFoundException("No such file in the backup.", path);
        }

        if (!response.IsSuccessStatusCode)
        {
            using (response)
                throw await PeerClient.ErrorAsync(response, cancellationToken);
        }

        return new ResponseStream(response, await response.Content.ReadAsStreamAsync(cancellationToken));
    }

    public async IAsyncEnumerable<BackupObject> ListAsync(string folder, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var files = await client.GetAsync<List<BackupObject>>(ticket, $"{Base}/files?{Query("folder", folder)}", cancellationToken);
        foreach (var file in files)
            yield return file;
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(ticket, b => new HttpRequestMessage(HttpMethod.Delete, new Uri(b, $"{Base}/file?{Query("path", path)}")),
            HttpCompletionOption.ResponseContentRead, cancellationToken);
        await EnsureAsync(response, path, cancellationToken);
    }

    public async Task DeleteFolderAsync(string folder, CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(ticket, b => new HttpRequestMessage(HttpMethod.Delete, new Uri(b, $"{Base}/folder?{Query("path", folder)}")),
            HttpCompletionOption.ResponseContentRead, cancellationToken);
        await EnsureAsync(response, folder, cancellationToken);
    }

    private static async Task EnsureAsync(HttpResponseMessage response, string path, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;
        var error = await PeerClient.ErrorAsync(response, cancellationToken);
        if (error.Code == ErrorCodes.StorageConflict)
            throw new StorageConflictException(path);
        if (error is ArcanistException.NotFound)
            throw new FileNotFoundException("No such file in the backup.", path);
        throw error;
    }

    /// <summary>A response's body that disposes the response with it.</summary>
    private sealed class ResponseStream(HttpResponseMessage response, Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
