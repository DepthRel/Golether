using System.Buffers.Binary;
using System.Collections.Concurrent;
using Golether.Core.Identity;
using Golether.Core.Media;
using Golether.Media.Streaming.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.Media.Streaming.Sharing;

/// <summary>
/// Exchanges chunks of the shared file between participants so the host uploads less.
/// </summary>
/// <remarks>
/// <para>Messages (big-endian): <c>1</c> have (media key, ranges); <c>2</c> request (media key, request id, chunk);
/// <c>3</c> data (request id, offset, total, bytes); <c>4</c> miss (request id).</para>
/// <para>Received chunks are not trusted here: <see cref="SwarmChunkSource"/> checks them against the hash from the
/// host.</para>
/// </remarks>
public sealed class PeerChunkExchange : IAsyncDisposable
{
    /// <summary>
    /// Message type: the chunks the sender has.
    /// </summary>
    internal const byte HaveMessage = 1;

    /// <summary>
    /// Message type: a chunk request.
    /// </summary>
    internal const byte RequestMessage = 2;

    /// <summary>
    /// Message type: a part of a chunk.
    /// </summary>
    internal const byte DataMessage = 3;

    /// <summary>
    /// Message type: the chunk is not available.
    /// </summary>
    internal const byte MissMessage = 4;

    /// <summary>
    /// The size of the data message header.
    /// </summary>
    private const int DataHeaderSize = 13;

    /// <summary>
    /// The transport.
    /// </summary>
    private readonly IPeerMessageTransport _transport;

    /// <summary>
    /// Returns a chunk this device has, or <see langword="null"/>.
    /// </summary>
    private readonly Func<long, byte[]?> _tryGetChunk;

    /// <summary>
    /// Lists the chunk ranges this device has.
    /// </summary>
    private readonly Func<int, IReadOnlyList<(long Start, long Count)>> _available;

    /// <summary>
    /// The options.
    /// </summary>
    private readonly PeerChunkExchangeOptions _options;

    /// <summary>
    /// The time provider.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// The media key.
    /// </summary>
    private readonly ulong _mediaKey;

    /// <summary>
    /// What each participant announced, and when.
    /// </summary>
    private readonly ConcurrentDictionary<PeerId, Announcement> _announcements = new();

    /// <summary>
    /// Requests of this device by id.
    /// </summary>
    private readonly ConcurrentDictionary<uint, Download> _downloads = new();

    /// <summary>
    /// Uploads in progress by participant.
    /// </summary>
    private readonly ConcurrentDictionary<PeerId, int> _uploads = new();

    /// <summary>
    /// Stops the exchange.
    /// </summary>
    private readonly CancellationTokenSource _stop = new();

    /// <summary>
    /// The announcement timer.
    /// </summary>
    private readonly ITimer _announcer;

    /// <summary>
    /// The next request id.
    /// </summary>
    private int _nextRequest;

    /// <summary>
    /// Backing field of <see cref="BytesReceived"/>.
    /// </summary>
    private long _bytesReceived;

    /// <summary>
    /// Backing field of <see cref="BytesSent"/>.
    /// </summary>
    private long _bytesSent;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerChunkExchange"/> class.
    /// </summary>
    /// <param name="media">The shared media.</param>
    /// <param name="transport">The channels to the participants.</param>
    /// <param name="tryGetChunk">Returns a chunk this device has, or <see langword="null"/>.</param>
    /// <param name="available">Lists up to the given number of chunk ranges this device has.</param>
    /// <param name="options">The options.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public PeerChunkExchange(
        MediaDescriptor media,
        IPeerMessageTransport transport,
        Func<long, byte[]?> tryGetChunk,
        Func<int, IReadOnlyList<(long Start, long Count)>> available,
        PeerChunkExchangeOptions? options = null,
        TimeProvider? timeProvider = null,
        ILogger? logger = null)
    {
        Media = media ?? throw new ArgumentNullException(nameof(media));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _tryGetChunk = tryGetChunk ?? throw new ArgumentNullException(nameof(tryGetChunk));
        _available = available ?? throw new ArgumentNullException(nameof(available));
        _options = options ?? new PeerChunkExchangeOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
        _mediaKey = ChunkProtocol.GetMediaKey(media);
        _transport.MessageReceived += OnMessage;
        _announcer = _timeProvider.CreateTimer(_ => Announce(), null, TimeSpan.Zero, _options.AnnounceInterval);
    }

    /// <summary>
    /// Gets the shared media.
    /// </summary>
    public MediaDescriptor Media { get; }

    /// <summary>
    /// Gets the chunk bytes received from participants (checked or not).
    /// </summary>
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    /// <summary>
    /// Gets the chunk bytes sent to participants.
    /// </summary>
    public long BytesSent => Interlocked.Read(ref _bytesSent);

    /// <summary>
    /// Returns the participants that announced a chunk recently.
    /// </summary>
    /// <param name="index">The chunk index.</param>
    /// <returns>The participants.</returns>
    public IReadOnlyList<PeerId> PeersHaving(long index)
    {
        var now = _timeProvider.GetUtcNow();
        var connected = _transport.ConnectedPeers;
        return _announcements
            .Where(a => now - a.Value.Time < _options.AnnouncementLifetime && connected.Contains(a.Key)
                        && a.Value.Ranges.Any(r => index >= r.Start && index < r.Start + r.Count))
            .Select(a => a.Key)
            .ToArray();
    }

    /// <summary>
    /// Asks participants that have a chunk for it.
    /// </summary>
    /// <param name="index">The chunk index.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The received bytes (unchecked), or <see langword="null"/> when no participant delivered them.</returns>
    public async Task<(PeerId Peer, byte[] Data)?> TryFetchAsync(long index, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Media.ChunkCount);
        var candidates = PeersHaving(index).OrderBy(_ => Random.Shared.Next()).ToArray();
        foreach (var peer in candidates)
        {
            var data = await TryFetchFromAsync(peer, index, cancellationToken).ConfigureAwait(false);
            if (data is not null)
            {
                return (peer, data);
            }
        }

        return null;
    }

    /// <summary>
    /// Sends the list of chunks this device has to every participant.
    /// </summary>
    public void Announce()
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }

        var ranges = _available(_options.MaxRanges);
        var message = new byte[11 + (Math.Min(ranges.Count, _options.MaxRanges) * 8)];
        message[0] = HaveMessage;
        BinaryPrimitives.WriteUInt64BigEndian(message.AsSpan(1), _mediaKey);
        var count = 0;
        foreach (var (start, length) in ranges.Take(_options.MaxRanges))
        {
            BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(11 + (count * 8)), (uint)start);
            BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(15 + (count * 8)), (uint)length);
            count++;
        }

        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(9), (ushort)count);
        foreach (var peer in _transport.ConnectedPeers)
        {
            _transport.TrySend(peer, message);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }

        _transport.MessageReceived -= OnMessage;
        await _stop.CancelAsync().ConfigureAwait(false);
        await _announcer.DisposeAsync().ConfigureAwait(false);
        foreach (var download in _downloads.Values)
        {
            download.Fail();
        }
    }

    /// <summary>
    /// Requests a chunk from one participant.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="index">The chunk index.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bytes, or <see langword="null"/>.</returns>
    private async Task<byte[]?> TryFetchFromAsync(PeerId peer, long index, CancellationToken cancellationToken)
    {
        var id = (uint)Interlocked.Increment(ref _nextRequest);
        var download = new Download(peer, index, Media.GetChunkLength(index), _timeProvider);
        _downloads[id] = download;
        try
        {
            var request = new byte[17];
            request[0] = RequestMessage;
            BinaryPrimitives.WriteUInt64BigEndian(request.AsSpan(1), _mediaKey);
            BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(9), id);
            BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(13), (uint)index);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);

            // A full channel buffer delays the request; a closed channel ends it.
            while (!_transport.TrySend(peer, request))
            {
                if (!_transport.ConnectedPeers.Contains(peer) || _timeProvider.GetUtcNow() - download.LastProgress > _options.StallTimeout)
                {
                    return null;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(10), _timeProvider, linked.Token).ConfigureAwait(false);
            }

            while (!download.Completion.IsCompleted)
            {
                await Task.WhenAny(download.Completion, Task.Delay(TimeSpan.FromMilliseconds(250), _timeProvider, linked.Token)).ConfigureAwait(false);
                if (!download.Completion.IsCompleted && _timeProvider.GetUtcNow() - download.LastProgress > _options.StallTimeout)
                {
                    _logger.LogDebug("Chunk {Index} from {Peer} stalled", index, peer.ToShortString());
                    return null;
                }
            }

            return await download.Completion.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            _downloads.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Handles a message of a participant.
    /// </summary>
    /// <param name="sender">The transport.</param>
    /// <param name="message">The message.</param>
    private void OnMessage(object? sender, PeerMessage message)
    {
        var data = message.Data.Span;
        if (data.IsEmpty || _stop.IsCancellationRequested)
        {
            return;
        }

        switch (data[0])
        {
            case HaveMessage when data.Length >= 11 && BinaryPrimitives.ReadUInt64BigEndian(data[1..]) == _mediaKey:
                var count = Math.Min(BinaryPrimitives.ReadUInt16BigEndian(data[9..]), _options.MaxRanges);
                if (data.Length < 11 + (count * 8))
                {
                    return;
                }

                var ranges = new (long Start, long Count)[count];
                for (var i = 0; i < count; i++)
                {
                    ranges[i] = (BinaryPrimitives.ReadUInt32BigEndian(data[(11 + (i * 8))..]), BinaryPrimitives.ReadUInt32BigEndian(data[(15 + (i * 8))..]));
                }

                _announcements[message.Peer] = new Announcement(ranges, _timeProvider.GetUtcNow());
                break;

            case RequestMessage when data.Length == 17 && BinaryPrimitives.ReadUInt64BigEndian(data[1..]) == _mediaKey:
                var id = BinaryPrimitives.ReadUInt32BigEndian(data[9..]);
                var index = (long)BinaryPrimitives.ReadUInt32BigEndian(data[13..]);
                StartUpload(message.Peer, id, index);
                break;

            case DataMessage when data.Length > DataHeaderSize:
                var dataId = BinaryPrimitives.ReadUInt32BigEndian(data[1..]);
                if (_downloads.TryGetValue(dataId, out var download) && download.Peer == message.Peer)
                {
                    var offset = BinaryPrimitives.ReadUInt32BigEndian(data[5..]);
                    var total = BinaryPrimitives.ReadUInt32BigEndian(data[9..]);
                    var payload = data[DataHeaderSize..];
                    if (download.Append(offset, total, payload))
                    {
                        Interlocked.Add(ref _bytesReceived, payload.Length);
                    }
                    else
                    {
                        download.Fail();
                    }
                }

                break;

            case MissMessage when data.Length == 5:
                if (_downloads.TryGetValue(BinaryPrimitives.ReadUInt32BigEndian(data[1..]), out var missed) && missed.Peer == message.Peer)
                {
                    missed.Fail();
                }

                break;
        }
    }

    /// <summary>
    /// Serves a chunk to a participant in the background, or answers "miss".
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="id">The request id.</param>
    /// <param name="index">The chunk index.</param>
    private void StartUpload(PeerId peer, uint id, long index)
    {
        var busy = _uploads.AddOrUpdate(peer, 1, (_, n) => n + 1);
        var chunk = busy <= _options.UploadsPerPeer && index < Media.ChunkCount ? _tryGetChunk(index) : null;
        if (chunk is null || chunk.Length != Media.GetChunkLength(index))
        {
            _uploads.AddOrUpdate(peer, 0, (_, n) => n - 1);
            var miss = new byte[5];
            miss[0] = MissMessage;
            BinaryPrimitives.WriteUInt32BigEndian(miss.AsSpan(1), id);
            _transport.TrySend(peer, miss);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await UploadAsync(peer, id, chunk).ConfigureAwait(false);
            }
            finally
            {
                _uploads.AddOrUpdate(peer, 0, (_, n) => n - 1);
            }
        });
    }

    /// <summary>
    /// Sends a chunk in parts, waiting while the channel buffer is full.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="id">The request id.</param>
    /// <param name="chunk">The chunk.</param>
    /// <returns>A task that completes when the chunk was sent or given up.</returns>
    private async Task UploadAsync(PeerId peer, uint id, byte[] chunk)
    {
        var buffer = new byte[DataHeaderSize + _options.FragmentSize];
        buffer[0] = DataMessage;
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(1), id);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(9), (uint)chunk.Length);
        for (var offset = 0; offset < chunk.Length;)
        {
            var size = Math.Min(_options.FragmentSize, chunk.Length - offset);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(5), (uint)offset);
            chunk.AsSpan(offset, size).CopyTo(buffer.AsSpan(DataHeaderSize));
            var waitedSince = _timeProvider.GetUtcNow();
            while (!_transport.TrySend(peer, buffer.AsSpan(0, DataHeaderSize + size)))
            {
                if (_stop.IsCancellationRequested || _timeProvider.GetUtcNow() - waitedSince > _options.StallTimeout
                    || !_transport.ConnectedPeers.Contains(peer))
                {
                    return;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(10), _timeProvider, _stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            offset += size;
            Interlocked.Add(ref _bytesSent, size);
        }
    }

    /// <summary>
    /// The chunks a participant announced.
    /// </summary>
    /// <param name="Ranges">The chunk ranges.</param>
    /// <param name="Time">The time of the announcement.</param>
    private sealed record Announcement((long Start, long Count)[] Ranges, DateTimeOffset Time);

    /// <summary>
    /// A chunk being received.
    /// </summary>
    private sealed class Download
    {
        /// <summary>
        /// The received bytes.
        /// </summary>
        private readonly byte[] _data;

        /// <summary>
        /// Completes with the chunk or <see langword="null"/>.
        /// </summary>
        private readonly TaskCompletionSource<byte[]?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// The time provider.
        /// </summary>
        private readonly TimeProvider _timeProvider;

        /// <summary>
        /// Guards the progress.
        /// </summary>
        private readonly Lock _gate = new();

        /// <summary>
        /// The number of bytes received in order.
        /// </summary>
        private int _received;

        /// <summary>
        /// Initializes a new instance of the <see cref="Download"/> class.
        /// </summary>
        /// <param name="peer">The participant asked.</param>
        /// <param name="index">The chunk index.</param>
        /// <param name="length">The expected length.</param>
        /// <param name="timeProvider">The time provider.</param>
        public Download(PeerId peer, long index, int length, TimeProvider timeProvider)
        {
            Peer = peer;
            Index = index;
            _data = new byte[length];
            _timeProvider = timeProvider;
            LastProgress = timeProvider.GetUtcNow();
        }

        /// <summary>
        /// Gets the participant asked.
        /// </summary>
        public PeerId Peer { get; }

        /// <summary>
        /// Gets the chunk index.
        /// </summary>
        public long Index { get; }

        /// <summary>
        /// Gets the time of the last received part.
        /// </summary>
        public DateTimeOffset LastProgress { get; private set; }

        /// <summary>
        /// Gets the result.
        /// </summary>
        public Task<byte[]?> Completion => _completion.Task;

        /// <summary>
        /// Adds a part; parts must arrive in order (the channel is ordered).
        /// </summary>
        /// <param name="offset">The offset of the part.</param>
        /// <param name="total">The announced total length.</param>
        /// <param name="payload">The part.</param>
        /// <returns><see langword="false"/> when the part does not fit.</returns>
        public bool Append(uint offset, uint total, ReadOnlySpan<byte> payload)
        {
            lock (_gate)
            {
                if (total != _data.Length || offset != _received || offset + (long)payload.Length > _data.Length)
                {
                    return false;
                }

                payload.CopyTo(_data.AsSpan((int)offset));
                _received += payload.Length;
                LastProgress = _timeProvider.GetUtcNow();
                if (_received == _data.Length)
                {
                    _completion.TrySetResult(_data);
                }

                return true;
            }
        }

        /// <summary>
        /// Gives up the download.
        /// </summary>
        public void Fail() => _completion.TrySetResult(null);
    }
}
