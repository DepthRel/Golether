using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.Transports.Relay;

/// <summary>
/// Carries several independent connections over one stream. The cameras and voices of a participant make more than
/// a dozen connections to the relay of the host; each one as a stream of its own is a separate connection through
/// the routers, and through a rendezvous server a separate meeting. With the multiplexer they share one.
/// </summary>
/// <remarks>
/// <para>
/// A frame is a header of 7 bytes (the channel number, 4 bytes big-endian; the kind, 1 byte; the length of the
/// payload, 2 bytes big-endian) and the payload of at most <see cref="MaxPayload"/> bytes. Kinds: <c>1</c> open (the
/// side that opened the stream starts a channel), <c>2</c> data, <c>3</c> close (either side; nothing more follows on
/// that channel).
/// </para>
/// <para>
/// What arrives is not trusted: a frame of an unknown kind or with an oversized payload ends the whole stream, a
/// channel beyond <see cref="MaxChannels"/> is closed at once, data for a channel that does not exist is dropped.
/// A channel whose reader is slow holds the others back instead of taking memory without a limit.
/// </para>
/// </remarks>
public sealed class RelayMultiplexer : IAsyncDisposable
{
    /// <summary>
    /// The largest number of channels at once: the relay of the host does not grant more allocations.
    /// </summary>
    public const int MaxChannels = 16;

    /// <summary>
    /// The largest payload of a frame, in bytes.
    /// </summary>
    public const int MaxPayload = 16 * 1024;

    /// <summary>
    /// The length of a frame header.
    /// </summary>
    private const int HeaderLength = 7;

    /// <summary>
    /// The frame that starts a channel.
    /// </summary>
    private const byte KindOpen = 1;

    /// <summary>
    /// The frame that carries bytes of a channel.
    /// </summary>
    private const byte KindData = 2;

    /// <summary>
    /// The frame that ends a channel.
    /// </summary>
    private const byte KindClose = 3;

    /// <summary>
    /// The shared stream.
    /// </summary>
    private readonly Stream _stream;

    /// <summary>
    /// Takes the channels the other side opens, or <see langword="null"/> on the side that opens them.
    /// </summary>
    private readonly Func<Stream, Task>? _accept;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// The open channels by number.
    /// </summary>
    private readonly ConcurrentDictionary<uint, Channel> _channels = new();

    /// <summary>
    /// Serializes the frames written to the shared stream.
    /// </summary>
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    /// <summary>
    /// Cancelled when the multiplexer ends.
    /// </summary>
    private readonly CancellationTokenSource _ending = new();

    /// <summary>
    /// The number of the channel opened last.
    /// </summary>
    private uint _lastChannel;

    /// <summary>
    /// The loop that reads the shared stream.
    /// </summary>
    private Task _reading = Task.CompletedTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="RelayMultiplexer"/> class.
    /// </summary>
    /// <param name="stream">The shared stream; ownership is transferred.</param>
    /// <param name="accept">
    /// Serves a channel the other side opened until it ends, or <see langword="null"/> on the side that opens the
    /// channels itself. The multiplexer closes the channel when the task completes.
    /// </param>
    /// <param name="logger">The logger.</param>
    public RelayMultiplexer(Stream stream, Func<Stream, Task>? accept = null, ILogger? logger = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _accept = accept;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Gets a value indicating whether the shared stream still works.
    /// </summary>
    public bool IsAlive => !_ending.IsCancellationRequested;

    /// <summary>
    /// Gets a task that completes when the shared stream ended.
    /// </summary>
    public Task Completion => _reading;

    /// <summary>
    /// Starts reading the shared stream.
    /// </summary>
    public void Start() => _reading = Task.Run(ReadLoopAsync);

    /// <summary>
    /// Opens a channel to the other side.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The channel; closing it ends the channel on both sides.</returns>
    /// <exception cref="IOException">The shared stream has ended or too many channels are open.</exception>
    public async Task<Stream> OpenAsync(CancellationToken cancellationToken)
    {
        if (!IsAlive)
        {
            throw new IOException("The shared relay stream has ended.");
        }

        if (_channels.Count >= MaxChannels)
        {
            throw new IOException("Too many relay channels are open.");
        }

        var channel = new Channel(this, Interlocked.Increment(ref _lastChannel));
        _channels[channel.Id] = channel;
        try
        {
            await SendAsync(channel.Id, KindOpen, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _channels.TryRemove(channel.Id, out _);
            channel.EndInbound();
            throw;
        }

        return channel;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await EndAsync().ConfigureAwait(false);
        try
        {
            await _reading.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The loop has ended; why is not interesting any more.
        }
    }

    /// <summary>
    /// Ends the multiplexer: closes the shared stream and every channel.
    /// </summary>
    /// <returns>A task that completes when everything is closed.</returns>
    private async Task EndAsync()
    {
        if (_ending.IsCancellationRequested)
        {
            return;
        }

        await _ending.CancelAsync().ConfigureAwait(false);
        foreach (var channel in _channels.Values)
        {
            channel.EndInbound();
        }

        _channels.Clear();
        await _stream.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Writes one frame.
    /// </summary>
    /// <param name="channel">The channel number.</param>
    /// <param name="kind">The kind of the frame.</param>
    /// <param name="payload">The payload, at most <see cref="MaxPayload"/> bytes.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the frame is flushed.</returns>
    /// <exception cref="IOException">The shared stream has ended.</exception>
    private async Task SendAsync(uint channel, byte kind, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var frame = new byte[HeaderLength + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame, channel);
        frame[4] = kind;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(5), (ushort)payload.Length);
        payload.CopyTo(frame.AsMemory(HeaderLength));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _ending.Token);
        try
        {
            await _sendGate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                // One write per frame: the voice packet inside leaves at once and whole.
                await _stream.WriteAsync(frame, linked.Token).ConfigureAwait(false);
                await _stream.FlushAsync(linked.Token).ConfigureAwait(false);
            }
            finally
            {
                _sendGate.Release();
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("The shared relay stream has ended.");
        }
        catch (ObjectDisposedException ex)
        {
            throw new IOException("The shared relay stream has ended.", ex);
        }
    }

    /// <summary>
    /// Reads frames until the shared stream ends and hands them to their channels.
    /// </summary>
    /// <returns>A task that completes when the shared stream ended.</returns>
    private async Task ReadLoopAsync()
    {
        var token = _ending.Token;
        var header = new byte[HeaderLength];
        var payload = new byte[MaxPayload];
        try
        {
            while (true)
            {
                var read = await _stream.ReadAtLeastAsync(header, HeaderLength, throwOnEndOfStream: false, token).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                if (read < HeaderLength)
                {
                    throw new InvalidDataException("The relay stream ended inside a frame header.");
                }

                var id = BinaryPrimitives.ReadUInt32BigEndian(header);
                var kind = header[4];
                var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(5));
                if (length > MaxPayload || kind is not (KindOpen or KindData or KindClose) || (kind != KindData && length != 0))
                {
                    throw new InvalidDataException($"A relay frame of kind {kind} with {length} bytes is not valid.");
                }

                if (length > 0 && await _stream.ReadAtLeastAsync(payload.AsMemory(0, length), length, throwOnEndOfStream: false, token).ConfigureAwait(false) < length)
                {
                    throw new InvalidDataException("The relay stream ended inside a frame.");
                }

                switch (kind)
                {
                    case KindOpen:
                        await AcceptAsync(id, token).ConfigureAwait(false);
                        break;
                    case KindData when _channels.TryGetValue(id, out var channel):
                        await channel.ReceiveAsync(payload.AsMemory(0, length), token).ConfigureAwait(false);
                        break;
                    case KindClose when _channels.TryRemove(id, out var closed):
                        closed.EndInbound();
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
        {
            if (!token.IsCancellationRequested)
            {
                _logger.LogInformation("The shared relay stream ended: {Reason}", ex.Message);
            }
        }
        finally
        {
            await EndAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Starts a channel the other side opened, or refuses it.
    /// </summary>
    /// <param name="id">The channel number.</param>
    /// <param name="cancellationToken">Stops the multiplexer.</param>
    /// <returns>A task that completes when the channel is being served or was refused.</returns>
    private async Task AcceptAsync(uint id, CancellationToken cancellationToken)
    {
        if (_accept is not { } accept || _channels.Count >= MaxChannels || _channels.ContainsKey(id))
        {
            // Nobody serves channels here, there are too many, or the number is taken: the other side is told.
            await SendAsync(id, KindClose, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
            return;
        }

        var channel = new Channel(this, id);
        _channels[id] = channel;
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await accept(channel).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _logger.LogDebug("A relay channel ended: {Reason}", ex.Message);
                }
                finally
                {
                    await channel.DisposeAsync().ConfigureAwait(false);
                }
            },
            CancellationToken.None);
    }

    /// <summary>
    /// One connection inside the shared stream.
    /// </summary>
    private sealed class Channel : Stream
    {
        /// <summary>
        /// The multiplexer.
        /// </summary>
        private readonly RelayMultiplexer _owner;

        /// <summary>
        /// What arrived for this channel and was not read yet. A full buffer makes the multiplexer wait.
        /// </summary>
        private readonly Pipe _inbound = new(new PipeOptions(pauseWriterThreshold: 256 * 1024, resumeWriterThreshold: 128 * 1024, useSynchronizationContext: false));

        /// <summary>
        /// The reading end of <see cref="_inbound"/> as a stream.
        /// </summary>
        private readonly Stream _reader;

        /// <summary>
        /// 1 once the channel was closed on this side.
        /// </summary>
        private int _closed;

        /// <summary>
        /// Initializes a new instance of the <see cref="Channel"/> class.
        /// </summary>
        /// <param name="owner">The multiplexer.</param>
        /// <param name="id">The channel number.</param>
        public Channel(RelayMultiplexer owner, uint id)
        {
            _owner = owner;
            Id = id;
            _reader = _inbound.Reader.AsStream();
        }

        /// <summary>
        /// Gets the channel number.
        /// </summary>
        public uint Id { get; }

        /// <inheritdoc />
        public override bool CanRead => true;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => true;

        /// <inheritdoc />
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <summary>
        /// Takes bytes that arrived for this channel.
        /// </summary>
        /// <param name="data">The bytes.</param>
        /// <param name="cancellationToken">Stops the multiplexer.</param>
        /// <returns>A task that completes when the bytes are buffered.</returns>
        public async ValueTask ReceiveAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            if (_closed != 0)
            {
                return;
            }

            try
            {
                await _inbound.Writer.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // The channel was closed on this side a moment ago: what still arrives is dropped.
            }
        }

        /// <summary>
        /// Marks the end of what arrives: the reader sees the end of the stream.
        /// </summary>
        public void EndInbound() => _inbound.Writer.Complete();

        /// <inheritdoc />
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _reader.ReadAsync(buffer, cancellationToken);

        /// <inheritdoc />
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _reader.ReadAsync(buffer, offset, count, cancellationToken);

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => _reader.Read(buffer, offset, count);

        /// <inheritdoc />
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (!buffer.IsEmpty)
            {
                if (_closed != 0)
                {
                    throw new IOException("The relay channel is closed.");
                }

                var part = buffer[..Math.Min(buffer.Length, MaxPayload)];
                await _owner.SendAsync(Id, KindData, part, cancellationToken).ConfigureAwait(false);
                buffer = buffer[part.Length..];
            }
        }

        /// <inheritdoc />
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
            => WriteAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc />
        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
            {
                return;
            }

            if (_owner._channels.TryRemove(new KeyValuePair<uint, Channel>(Id, this)))
            {
                try
                {
                    await _owner.SendAsync(Id, KindClose, ReadOnlyMemory<byte>.Empty, CancellationToken.None).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    // The shared stream is gone, and the channel with it.
                }
            }

            await _inbound.Writer.CompleteAsync().ConfigureAwait(false);
            await _inbound.Reader.CompleteAsync().ConfigureAwait(false);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            base.Dispose(disposing);
        }
    }
}
