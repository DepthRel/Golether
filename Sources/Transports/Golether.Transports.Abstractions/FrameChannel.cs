using System.Buffers.Binary;

namespace Golether.Transports;

/// <summary>
/// Length-prefixed frames over a byte stream: a 4-byte big-endian length followed by the payload.
/// </summary>
/// <remarks>
/// Sends are serialized, so several tasks may send concurrently; receiving must be done by one task.
/// </remarks>
public sealed class FrameChannel : IAsyncDisposable
{
    /// <summary>
    /// The default maximum frame size: 1 MiB.
    /// </summary>
    public const int DEFAULT_MAX_FRAME_SIZE = 1024 * 1024;

    /// <summary>
    /// The underlying stream.
    /// </summary>
    private readonly Stream _stream;

    /// <summary>
    /// The maximum accepted frame size.
    /// </summary>
    private readonly int _maxFrameSize;

    /// <summary>
    /// Serializes writers.
    /// </summary>
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    /// <summary>
    /// Cancelled when the channel is closed, so the writers waiting for their turn do not wait for ever.
    /// </summary>
    private readonly CancellationTokenSource _closing = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="FrameChannel"/> class.
    /// </summary>
    /// <param name="stream">The stream; ownership is transferred.</param>
    /// <param name="maxFrameSize">The maximum frame size in bytes.</param>
    public FrameChannel(Stream stream, int maxFrameSize = DEFAULT_MAX_FRAME_SIZE)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrameSize, 1);
        _maxFrameSize = maxFrameSize;
    }

    /// <summary>
    /// Sends a frame.
    /// </summary>
    /// <param name="payload">The payload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the frame is flushed.</returns>
    /// <exception cref="ArgumentException">The payload exceeds the maximum frame size.</exception>
    public ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        => SendAsync(payload, Timeout.InfiniteTimeSpan, cancellationToken);

    /// <summary>
    /// Sends a frame that may take a limited time to be written. The limit covers the writing alone: the wait behind
    /// the frames queued earlier is not counted, so a small frame is not given up on because a large one in front of
    /// it travels slowly. Each of those frames has its own limit, which bounds the wait.
    /// </summary>
    /// <param name="payload">The payload.</param>
    /// <param name="writeTimeout">The time the write may take, or <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the frame is flushed.</returns>
    /// <exception cref="ArgumentException">The payload exceeds the maximum frame size.</exception>
    /// <exception cref="OperationCanceledException">The write took longer than allowed or was cancelled.</exception>
    /// <exception cref="ObjectDisposedException">The channel was closed.</exception>
    public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, TimeSpan writeTimeout, CancellationToken cancellationToken)
    {
        if (payload.Length > _maxFrameSize)
        {
            throw new ArgumentException($"The frame of {payload.Length} bytes exceeds the limit of {_maxFrameSize} bytes.", nameof(payload));
        }

        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        ObjectDisposedException.ThrowIf(_closing.IsCancellationRequested, this);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closing.Token);
        try
        {
            await _sendGate.WaitAsync(limit.Token).ConfigureAwait(false);
            try
            {
                // The time starts when the turn comes, not when the frame joined the queue.
                limit.CancelAfter(writeTimeout);
                await _stream.WriteAsync(header, limit.Token).ConfigureAwait(false);
                await _stream.WriteAsync(payload, limit.Token).ConfigureAwait(false);
                await _stream.FlushAsync(limit.Token).ConfigureAwait(false);
            }
            finally
            {
                _sendGate.Release();
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(FrameChannel));
        }
    }

    /// <summary>
    /// Receives the next frame.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The payload, or <see langword="null"/> when the peer closed the stream between frames.</returns>
    /// <exception cref="InvalidDataException">The frame is too large or the stream ended inside a frame.</exception>
    public async ValueTask<byte[]?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var header = new byte[4];
        var read = await ReadAtLeastAsync(header, cancellationToken).ConfigureAwait(false);
        if (read == 0)
        {
            return null;
        }

        if (read < header.Length)
        {
            throw new InvalidDataException("The stream ended inside a frame header.");
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length < 0 || length > _maxFrameSize)
        {
            throw new InvalidDataException($"The frame length {length} is out of range.");
        }

        var payload = new byte[length];
        if (await ReadAtLeastAsync(payload, cancellationToken).ConfigureAwait(false) < length)
        {
            throw new InvalidDataException("The stream ended inside a frame.");
        }

        return payload;
    }

    /// <summary>
    /// Closes the stream.
    /// </summary>
    /// <returns>A task that completes when the stream is closed.</returns>
    public async ValueTask DisposeAsync()
    {
        // The gate and the signal are not disposed: writers that wait for their turn or have just finished still
        // touch them, and neither holds anything that needs releasing.
        await _closing.CancelAsync().ConfigureAwait(false);
        await _stream.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Fills a buffer unless the stream ends.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of bytes read.</returns>
    private async ValueTask<int> ReadAtLeastAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        => buffer.Length == 0
            ? 0
            : await _stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
}
