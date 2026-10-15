using Arcanist.Core.Scanning;

namespace Arcanist.Core.Storage;

/// <summary>
/// A file a storage describes itself: its tags, content hash and artwork as another server already read them, so
/// the scanner catalogs it without downloading it.
/// </summary>
/// <param name="Artwork">Id of its artwork for <see cref="IDescribedStorage.ReadArtworkAsync"/>; null when it has none.</param>
public sealed record DescribedFile(
    string RelativePath,
    int CueTrack,
    int? StartMs,
    int? EndMs,
    long Size,
    DateTime ModifiedAt,
    string ContentHash,
    string? Artwork,
    AudioMetadata Metadata);

/// <summary>
/// Implemented by storages whose files are described by their owner instead of read (a server sharing through
/// the P2P network; later, the cloud). The scanner then takes the descriptions: listing a whole library costs a few
/// requests, and files are only transferred when played.
/// </summary>
public interface IDescribedStorage
{
    /// <summary>Every file, the rows of one path (tracks of a cue sheet) one after another.</summary>
    IAsyncEnumerable<DescribedFile> DescribeAsync(StorageLocation location, CancellationToken cancellationToken);

    /// <summary>The bytes of an artwork named by <see cref="DescribedFile.Artwork"/>; null when gone.</summary>
    Task<byte[]?> ReadArtworkAsync(StorageLocation location, string artwork, CancellationToken cancellationToken);
}
