namespace Cthangover.Mods.DebugClient.Mcp;

/// <summary>
/// Borrows the connected pipe for one MCP session. The SDK can dispose this view while the server keeps the
/// listener alive for the next client. Closing and disconnecting the underlying pipe are server operations.
/// </summary>
internal sealed class PipeSessionStream(Stream pipe) : Stream
{
    public override bool CanRead => pipe.CanRead;
    public override bool CanWrite => pipe.CanWrite;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => pipe.Read(buffer, offset, count);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => pipe.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => pipe.ReadAsync(buffer, cancellationToken);

    public override void Write(byte[] buffer, int offset, int count) => pipe.Write(buffer, offset, count);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => pipe.WriteAsync(buffer, offset, count, cancellationToken);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => pipe.WriteAsync(buffer, cancellationToken);

    public override void Flush() => pipe.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => pipe.FlushAsync(cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
