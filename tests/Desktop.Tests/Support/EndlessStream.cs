namespace PropGenerator.Desktop.Tests.Support;

/// <summary>
/// A body that, to the code reading it, does not end: zeros far past any limit the client has, or (stalled) nothing at
/// all until the read is cancelled, as a storage that keeps the connection open and sends no more would. The zeros do
/// end after <see cref="Far"/> bytes, so a client that reads it all fails its test instead of filling the memory.
/// </summary>
/// <param name="stalled">True to send nothing, false to send zeros.</param>
internal sealed class EndlessStream(bool stalled) : Stream
{
    /// <summary>Where the zeros end: far past the limits the tests give (a few megabytes).</summary>
    public const long Far = 64L * 1024 * 1024;

    /// <summary>Gets how many bytes were read from it.</summary>
    public long Sent { get; private set; }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (stalled)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        int n = (int)Math.Min(buffer.Length, Far - Sent);
        buffer.Span[..n].Clear();
        Sent += n;
        return n;
    }

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
