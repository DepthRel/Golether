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
    public const int DefaultMaxFrameSize = 1024 * 1024;

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
    /// Initializes a new instance of the <see cref="FrameChannel"/> class.
    /// </summary>
    /// <param name="stream">The stream; ownership is transferred.</param>
    /// <param name="maxFrameSize">The maximum frame size in bytes.</param>
    public FrameChannel(Stream stream, int maxFrameSize = DefaultMaxFrameSize)
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
    public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (payload.Length > _maxFrameSize)
        {
            throw new ArgumentException($"The frame of {payload.Length} bytes exceeds the limit of {_maxFrameSize} bytes.", nameof(payload));
        }

        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
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
        await _stream.DisposeAsync().ConfigureAwait(false);
        _sendGate.Dispose();
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
