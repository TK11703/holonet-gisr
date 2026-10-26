namespace Gisr.Web.Artifacts;

// Reports cumulative bytes read, throttled, so an upload can show progress while HttpClient pulls the file.
public sealed class ProgressStream(Stream inner, Action<long> onProgress, long reportEveryBytes = 128 * 1024) : Stream
{
    private long _total;
    private long _lastReported;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Track(inner.Read(buffer, offset, count));

    public override int Read(Span<byte> buffer) => Track(inner.Read(buffer));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Track(await inner.ReadAsync(buffer, cancellationToken));

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Track(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private int Track(int read)
    {
        if (read > 0)
        {
            _total += read;
        }

        // Always report the end of the stream (read == 0) so the bar reaches 100%.
        if (_total - _lastReported >= reportEveryBytes || (read == 0 && _total != _lastReported))
        {
            _lastReported = _total;
            onProgress(_total);
        }

        return read;
    }
}
