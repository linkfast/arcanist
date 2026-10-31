using System.Runtime.CompilerServices;
using Arcanist.Core.Application;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Extensions;
using Arcanist.Core.Storage;
using Arcanist.Extensibility;

namespace Arcanist.Core.Backups.Targets;

/// <summary>
/// Destinations an extension provides (<see cref="IBackupDestinationProvider"/>): their location is
/// <c>&lt;provider&gt;:&lt;the provider's location&gt;</c>. Without that provider running they are unavailable.
/// </summary>
internal sealed class ExtensionBackupTargets(ExtensionHost extensions, IServiceProvider services)
{
    /// <summary>The providers that may be used now.</summary>
    public IReadOnlyList<IBackupDestinationProvider> Providers =>
        services.GetServices<IBackupDestinationProvider>().Where(extensions.IsActive).ToList();

    public IBackupDestinationProvider? Provider(string id) => Providers.FirstOrDefault(p => p.Id == id);

    public static (string Provider, string Location) Split(string location)
    {
        var colon = location.IndexOf(':');
        return colon < 0 ? (location, "") : (location[..colon], location[(colon + 1)..]);
    }

    public IBackupTarget Open(BackupDestination destination)
    {
        var (id, location) = Split(destination.Location);
        return Provider(id) is { } provider
            ? new ExtensionTarget(provider.Open(location, destination.Settings))
            : new UnavailableTarget(ErrorCodes.CloudPlusRequired);
    }
}

/// <summary>A destination of an extension, as the server's backups write to one.</summary>
internal sealed class ExtensionTarget(IBackupStore store) : IBackupTarget
{
    public Task<string?> ProbeAsync(CancellationToken cancellationToken) => store.ProbeAsync(cancellationToken);

    public async Task<BackupObject?> StatAsync(string path, CancellationToken cancellationToken) =>
        await store.StatAsync(path, cancellationToken) is { } file ? new BackupObject(file.Path, file.Size) : null;

    public async Task WriteAsync(string path, Stream content, long length, CancellationToken cancellationToken)
    {
        try
        {
            await store.WriteAsync(path, content, length, cancellationToken);
        }
        catch (BackupFileExistsException)
        {
            throw new StorageConflictException(path);
        }
    }

    public async Task MoveAsync(string from, string to, CancellationToken cancellationToken)
    {
        try
        {
            await store.MoveAsync(from, to, cancellationToken);
        }
        catch (BackupFileExistsException)
        {
            throw new StorageConflictException(to);
        }
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken) => store.OpenReadAsync(path, cancellationToken);

    public async IAsyncEnumerable<BackupObject> ListAsync(string folder, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var file in store.ListAsync(folder, cancellationToken))
            yield return new BackupObject(file.Path, file.Size);
    }

    public Task<IReadOnlyList<string>> FoldersAsync(string folder, CancellationToken cancellationToken) => store.FoldersAsync(folder, cancellationToken);

    public Task DeleteAsync(string path, CancellationToken cancellationToken) => store.DeleteAsync(path, cancellationToken);

    public Task DeleteFolderAsync(string folder, CancellationToken cancellationToken) => store.DeleteFolderAsync(folder, cancellationToken);
}
