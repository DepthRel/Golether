using System.Buffers;

namespace Golether.Media.Streaming.Caching;

/// <summary>
/// A read-only, seekable <see cref="Stream"/> over <see cref="CachedMediaReader"/>, handed to the player.
/// </summary>
/// <remarks>
/// The player reads synchronously on its own demuxer thread, so blocking in <see cref="Read(byte[], int, int)"/> is
/// expected. <see cref="CancelPendingRead"/> unblocks a read when the player aborts it (for example on seek or stop).
/// </remarks>
public sealed class MediaReadStream : Stream
{
    /// <summary>
    /// The reader; not owned.
    /// </summary>
    private readonly CachedMediaReader _reader;

    /// <summary>
    /// Cancels blocked reads; replaced after each cancellation.
    /// </summary>
    private CancellationTokenSource _cancel = new();

    /// <summary>
    /// The current position.
    /// </summary>
    private long _position;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaReadStream"/> class.
    /// </summary>
    /// <param name="reader">The reader; the caller keeps ownership.</param>
    public MediaReadStream(CachedMediaReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => true;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => _reader.Media.Length;

    /// <inheritdoc />
    public override long Position
    {
        get => Interlocked.Read(ref _position);
        set => Seek(value, SeekOrigin.Begin);
    }

    /// <summary>
    /// Unblocks a pending read; it fails with <see cref="OperationCanceledException"/>.
    /// </summary>
    public void CancelPendingRead()
    {
        var previous = Interlocked.Exchange(ref _cancel, new CancellationTokenSource());
        previous.Cancel();
        previous.Dispose();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            var read = ReadAsync(rented.AsMemory(0, buffer.Length), CancellationToken.None).AsTask().GetAwaiter().GetResult();
            rented.AsSpan(0, read).CopyTo(buffer);
            return read;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancel.Token);
        var position = Position;
        var read = await _reader.ReadAsync(position, buffer, linked.Token).ConfigureAwait(false);
        Interlocked.CompareExchange(ref _position, position + read, position);
        return read;
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        if (target < 0 || target > Length)
        {
            throw new IOException($"Position {target} is outside the media of {Length} bytes.");
        }

        Interlocked.Exchange(ref _position, target);
        return target;
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancel.Dispose();
        }

        base.Dispose(disposing);
    }
}
