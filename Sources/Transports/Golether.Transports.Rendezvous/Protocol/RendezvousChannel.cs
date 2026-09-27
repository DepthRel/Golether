using System.Buffers.Binary;
using System.Text.Json;

namespace Golether.Transports.Rendezvous.Protocol;

/// <summary>
/// Sends and receives <see cref="RendezvousMessage"/> objects over a stream: a 4-byte big-endian length followed by
/// UTF-8 JSON of at most <see cref="RendezvousProtocol.MaxFrameSize"/> bytes.
/// </summary>
/// <remarks>
/// Sends are serialized, so several tasks may send concurrently; receiving must be done by one task.
/// </remarks>
public sealed class RendezvousChannel : IAsyncDisposable
{
    /// <summary>
    /// The JSON options of the protocol.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 8,
    };

    /// <summary>
    /// The underlying stream.
    /// </summary>
    private readonly Stream _stream;

    /// <summary>
    /// Serializes writers.
    /// </summary>
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="RendezvousChannel"/> class.
    /// </summary>
    /// <param name="stream">The stream; ownership is transferred.</param>
    public RendezvousChannel(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>
    /// Serializes a message.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The UTF-8 JSON.</returns>
    public static byte[] Serialize(RendezvousMessage message) => JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);

    /// <summary>
    /// Deserializes a message.
    /// </summary>
    /// <param name="payload">The UTF-8 JSON.</param>
    /// <returns>The message.</returns>
    /// <exception cref="InvalidDataException">The payload is not a valid message.</exception>
    public static RendezvousMessage Deserialize(ReadOnlySpan<byte> payload)
    {
        try
        {
            return JsonSerializer.Deserialize<RendezvousMessage>(payload, JsonOptions)
                ?? throw new InvalidDataException("The message is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The message is malformed: {ex.Message}", ex);
        }
        catch (NotSupportedException ex)
        {
            throw new InvalidDataException($"The message type is not supported: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Sends a message.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the message is flushed.</returns>
    public async ValueTask SendAsync(RendezvousMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var payload = Serialize(message);
        if (payload.Length > RendezvousProtocol.MaxFrameSize)
        {
            throw new ArgumentException($"The message of {payload.Length} bytes exceeds the limit of {RendezvousProtocol.MaxFrameSize} bytes.", nameof(message));
        }

        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, payload.Length);
        payload.CopyTo(frame, 4);
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    /// <summary>
    /// Receives the next message.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The message, or <see langword="null"/> when the other side closed the stream between messages.</returns>
    /// <exception cref="InvalidDataException">The other side sent invalid data.</exception>
    public async ValueTask<RendezvousMessage?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var header = new byte[4];
        var read = await _stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        if (read == 0)
        {
            return null;
        }

        if (read < header.Length)
        {
            throw new InvalidDataException("The stream ended inside a frame header.");
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is < 1 or > RendezvousProtocol.MaxFrameSize)
        {
            throw new InvalidDataException($"The frame length {length} is out of range.");
        }

        var payload = new byte[length];
        if (await _stream.ReadAtLeastAsync(payload, length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false) < length)
        {
            throw new InvalidDataException("The stream ended inside a frame.");
        }

        return Deserialize(payload);
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
}
