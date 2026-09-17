using System.Text.Json;
using Golether.Transports;

namespace Golether.Sync.Protocol;

/// <summary>
/// Sends and receives <see cref="SessionMessage"/> objects over a <see cref="FrameChannel"/>.
/// </summary>
public sealed class SessionMessageChannel : IAsyncDisposable
{
    /// <summary>
    /// The maximum size of a control message: 256 KiB.
    /// </summary>
    public const int MaxMessageSize = 256 * 1024;

    /// <summary>
    /// The JSON options of the protocol.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 16,
    };

    /// <summary>
    /// The frame channel.
    /// </summary>
    private readonly FrameChannel _frames;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionMessageChannel"/> class.
    /// </summary>
    /// <param name="stream">The control stream; ownership is transferred.</param>
    public SessionMessageChannel(Stream stream)
    {
        _frames = new FrameChannel(stream, MaxMessageSize);
    }

    /// <summary>
    /// Serializes a message.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The UTF-8 JSON.</returns>
    public static byte[] Serialize(SessionMessage message) => JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);

    /// <summary>
    /// Deserializes a message.
    /// </summary>
    /// <param name="payload">The UTF-8 JSON.</param>
    /// <returns>The message.</returns>
    /// <exception cref="InvalidDataException">The payload is not a valid message.</exception>
    public static SessionMessage Deserialize(ReadOnlySpan<byte> payload)
    {
        try
        {
            return JsonSerializer.Deserialize<SessionMessage>(payload, JsonOptions)
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
    public ValueTask SendAsync(SessionMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _frames.SendAsync(Serialize(message), cancellationToken);
    }

    /// <summary>
    /// Receives the next message.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The message, or <see langword="null"/> when the peer closed the stream.</returns>
    /// <exception cref="InvalidDataException">The peer sent invalid data.</exception>
    public async ValueTask<SessionMessage?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var payload = await _frames.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        return payload is null ? null : Deserialize(payload);
    }

    /// <summary>
    /// Closes the stream.
    /// </summary>
    /// <returns>A task that completes when the stream is closed.</returns>
    public ValueTask DisposeAsync() => _frames.DisposeAsync();
}
