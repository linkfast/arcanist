namespace Arcanist.Core.Storage.S3;

/// <summary>
/// A seekable, read-only view of an object that fetches only what is read: after a seek, a small range (tags at the
/// start or the end of a file, the samples of the content hash); when reading on from where the last read ended,
/// one request for the rest of the object (playing, converting). The scanner thus never downloads whole files.
/// </summary>
internal sealed class S3ReadStream(S3Client client, string key, long length) : Stream
{
    private const int SmallRange = 256 * 1024;

    private Stream? _body;
    private HttpResponseMessage? _response;
    private long _bodyEnd;
    private long _lastReadEnd = -1;
    private long _position;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => length + offset,
        };
        if (target < 0)
            throw new IOException("Seek before the start of the object.");
        _position = target;
        return _position;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position >= length || buffer.Length == 0)
            return 0;

        // The open response no longer covers the position: a seek happened, or a small range ran out.
        if (_body is not null && (_position != _lastReadEnd || _position >= _bodyEnd))
            CloseBody();

        if (_body is null)
        {
            // Reading on where the last read ended: the rest of the object. Otherwise just a little.
            var sequential = _position == _lastReadEnd;
            var to = sequential ? length - 1 : Math.Min(length - 1, _position + Math.Max(buffer.Length, SmallRange) - 1);
            _response = await client.GetAsync(key, _position, to, cancellationToken);
            _body = await _response.Content.ReadAsStreamAsync(cancellationToken);
            _bodyEnd = to + 1;
        }

        var read = await _body.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _bodyEnd - _position)], cancellationToken);
        if (read == 0)
        {
            CloseBody();
            throw new IOException("The object ended before its length.");
        }

        _position += read;
        _lastReadEnd = _position;
        return read;
    }

    private void CloseBody()
    {
        _body?.Dispose();
        _response?.Dispose();
        _body = null;
        _response = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            CloseBody();
        base.Dispose(disposing);
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
