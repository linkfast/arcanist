using System.Buffers;
using System.Security.Cryptography;

namespace Arcanist.Core.Scanning;

/// <summary>
/// Identifies a file by its audio data only, so re-tagging or moving it keeps the same hash. Large files are
/// sampled (start, middle and end plus the length) to keep scans of lossless libraries on network shares fast.
/// </summary>
public static class ContentHasher
{
    private const long FullHashLimit = 32 * 1024 * 1024;
    private const int SampleSize = 1024 * 1024;

    public static async Task<string> HashAsync(Stream stream, long offset, long length, CancellationToken cancellationToken)
    {
        // Formats whose audio region is unknown fall back to the whole file.
        if (length <= 0 || offset < 0 || offset + length > stream.Length)
        {
            offset = 0;
            length = stream.Length;
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(BitConverter.GetBytes(length));

        if (length <= FullHashLimit)
        {
            await AppendAsync(hash, stream, offset, length, cancellationToken);
        }
        else
        {
            await AppendAsync(hash, stream, offset, SampleSize, cancellationToken);
            await AppendAsync(hash, stream, offset + length / 2 - SampleSize / 2, SampleSize, cancellationToken);
            await AppendAsync(hash, stream, offset + length - SampleSize, SampleSize, cancellationToken);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static async Task AppendAsync(IncrementalHash hash, Stream stream, long offset, long count, CancellationToken cancellationToken)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (count > 0)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, count)), cancellationToken);
                if (read == 0)
                    break;

                hash.AppendData(buffer, 0, read);
                count -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
