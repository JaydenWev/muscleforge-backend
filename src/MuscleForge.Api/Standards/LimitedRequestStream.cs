namespace MuscleForge.Api.Standards;

internal sealed class RequestBodyTooLargeException : Exception;

// Counts bytes as consumers read, without buffering or reading before the endpoint needs them.
internal sealed class LimitedRequestStream(Stream inner, long limit) : Stream
{
    private long consumed;
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => consumed; set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, Bound(count)));
    public override int Read(Span<byte> buffer) => Count(inner.Read(buffer[..Bound(buffer.Length)]));
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await inner.ReadAsync(buffer[..Bound(buffer.Length)], cancellationToken));
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Count(await inner.ReadAsync(buffer.AsMemory(offset, Bound(count)), cancellationToken));
    private int Bound(int requested) => (int)Math.Min(requested, Math.Max(1, limit - consumed + 1));
    private int Count(int read) { consumed += read; if (consumed > limit) throw new RequestBodyTooLargeException(); return read; }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
