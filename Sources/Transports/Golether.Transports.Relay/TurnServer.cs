using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.Transports.Relay;

/// <summary>
/// Credentials of the relay, handed only to admitted participants over the session channel.
/// </summary>
/// <param name="Port">The TCP port of the relay.</param>
/// <param name="Username">The user name.</param>
/// <param name="Password">The password.</param>
public sealed record RelayCredentials(int Port, string Username, string Password)
{
    /// <summary>
    /// Creates random credentials.
    /// </summary>
    /// <param name="port">The TCP port of the relay.</param>
    /// <returns>The credentials.</returns>
    public static RelayCredentials CreateRandom(int port)
        => new(port, "golether-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant(),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_'));

    /// <summary>
    /// Builds the TURN address for a relay host, for example <c>turn://user:pass@203.0.113.7:47801?transport=tcp</c>.
    /// </summary>
    /// <param name="host">The address of the host as the participant reaches it.</param>
    /// <returns>The address.</returns>
    public string ToTurnUri(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var address = host.Contains(':', StringComparison.Ordinal) && !host.StartsWith('[') ? $"[{host}]" : host;
        return $"turn://{Uri.EscapeDataString(Username)}:{Uri.EscapeDataString(Password)}@{address}:{Port}?transport=tcp";
    }
}

/// <summary>
/// Settings of <see cref="TurnServer"/>.
/// </summary>
public sealed record TurnServerOptions
{
    /// <summary>
    /// Gets the credentials.
    /// </summary>
    public required RelayCredentials Credentials { get; init; }

    /// <summary>
    /// Gets the address to listen on.
    /// </summary>
    public IPAddress ListenAddress { get; init; } = IPAddress.IPv6Any;

    /// <summary>
    /// Gets the realm of the long-term credentials.
    /// </summary>
    public string Realm { get; init; } = "golether";

    /// <summary>
    /// Gets the maximum number of simultaneous allocations (a session has at most four participants, each with
    /// a couple of ICE components).
    /// </summary>
    public int MaxAllocations { get; init; } = 16;

    /// <summary>
    /// Gets the longest allocation lifetime granted.
    /// </summary>
    public TimeSpan MaxLifetime { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets the time a connection may stay without an allocation.
    /// </summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// A TURN server (RFC 5766) for TCP clients with UDP relays: enough for WebRTC through the host.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Every request that changes state needs the long-term credentials of the session.</item>
/// <item>A relay forwards only to addresses the client created a permission for, and only while the allocation lives.</item>
/// <item>The server does not see the media: WebRTC encrypts it end to end with DTLS-SRTP.</item>
/// </list>
/// </remarks>
public sealed class TurnServer : IAsyncDisposable
{
    /// <summary>
    /// The lifetime of a permission (RFC 5766).
    /// </summary>
    private static readonly TimeSpan PermissionLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The options.
    /// </summary>
    private readonly TurnServerOptions _options;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// The time provider.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The long-term key.
    /// </summary>
    private readonly byte[] _key;

    /// <summary>
    /// The listener.
    /// </summary>
    private readonly TcpListener _listener;

    /// <summary>
    /// Stops the server.
    /// </summary>
    private readonly CancellationTokenSource _stop = new();

    /// <summary>
    /// The live allocations.
    /// </summary>
    private readonly ConcurrentDictionary<Allocation, byte> _allocations = new();

    /// <summary>
    /// The accept loop.
    /// </summary>
    private Task _acceptLoop = Task.CompletedTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="TurnServer"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public TurnServer(TurnServerOptions options, TimeProvider? timeProvider = null, ILogger<TurnServer>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<TurnServer>.Instance;
        _key = Stun.LongTermKey(options.Credentials.Username, options.Realm, options.Credentials.Password);
        _listener = new TcpListener(options.ListenAddress, options.Credentials.Port);
        if (options.ListenAddress.Equals(IPAddress.IPv6Any))
        {
            _listener.Server.DualMode = true;
        }
    }

    /// <summary>
    /// Gets the listening port (useful when port 0 was requested).
    /// </summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>
    /// Gets the credentials with the actual listening port.
    /// </summary>
    public RelayCredentials Credentials => _options.Credentials with { Port = Port };

    /// <summary>
    /// Gets the number of live allocations.
    /// </summary>
    public int AllocationCount => _allocations.Count;

    /// <summary>
    /// Starts accepting clients.
    /// </summary>
    /// <exception cref="SocketException">The port is in use.</exception>
    public void Start()
    {
        _listener.Start();
        _acceptLoop = Task.Run(AcceptLoopAsync);
        _logger.LogInformation("TURN relay listens on port {Port}", Port);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        await _acceptLoop.ConfigureAwait(false);
        foreach (var allocation in _allocations.Keys)
        {
            allocation.Dispose();
        }

        _allocations.Clear();
        _stop.Dispose();
    }

    /// <summary>
    /// Accepts clients until disposal.
    /// </summary>
    /// <returns>A task that completes when the server stops.</returns>
    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    /// <summary>
    /// Serves one TCP client: one allocation at most (RFC 6062 style five-tuple).
    /// </summary>
    /// <param name="client">The client.</param>
    /// <returns>A task that completes when the client leaves.</returns>
    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            var session = new ClientSession(this, client, stream, RandomNumberGenerator.GetHexString(24, lowercase: true));
            try
            {
                await session.RunAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException or SocketException)
            {
                _logger.LogDebug("TURN client {Client} left: {Reason}", client.Client.RemoteEndPoint, ex.Message);
            }
            finally
            {
                if (session.Allocation is { } allocation)
                {
                    _allocations.TryRemove(allocation, out _);
                    allocation.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// The state of one TCP client.
    /// </summary>
    /// <param name="server">The server.</param>
    /// <param name="client">The TCP client.</param>
    /// <param name="stream">The TCP stream.</param>
    /// <param name="nonce">The nonce of this connection.</param>
    private sealed class ClientSession(TurnServer server, TcpClient client, NetworkStream stream, string nonce)
    {
        /// <summary>
        /// Serializes writes to the TCP stream.
        /// </summary>
        private readonly SemaphoreSlim _writeGate = new(1, 1);

        /// <summary>
        /// Gets the allocation of this client.
        /// </summary>
        public Allocation? Allocation { get; private set; }

        /// <summary>
        /// Reads STUN messages and ChannelData frames until the client leaves.
        /// </summary>
        /// <param name="cancellationToken">Stops the server.</param>
        /// <returns>A task that completes when the connection closes.</returns>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var header = new byte[4];
            var body = new byte[StunMessage.MaxSize];
            while (true)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (Allocation is null)
                {
                    idle.CancelAfter(server._options.IdleTimeout);
                }
                else if (Allocation.IsExpired)
                {
                    return;
                }

                await stream.ReadExactlyAsync(header, idle.Token).ConfigureAwait(false);
                var channel = BinaryPrimitives.ReadUInt16BigEndian(header);
                var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2));
                if ((header[0] & 0xC0) == 0x40)
                {
                    // ChannelData, padded to four bytes over TCP.
                    var padded = (length + 3) & ~3;
                    if (padded > body.Length)
                    {
                        throw new InvalidDataException("ChannelData is too large.");
                    }

                    await stream.ReadExactlyAsync(body.AsMemory(0, padded), idle.Token).ConfigureAwait(false);
                    await (Allocation?.SendToChannelAsync(channel, body.AsMemory(0, length)) ?? ValueTask.CompletedTask).ConfigureAwait(false);
                    continue;
                }

                var size = StunMessage.MessageSize(header);
                if (size < Stun.HeaderSize || size > StunMessage.MaxSize)
                {
                    throw new InvalidDataException("Not a TURN stream.");
                }

                header.CopyTo(body, 0);
                await stream.ReadExactlyAsync(body.AsMemory(4, size - 4), idle.Token).ConfigureAwait(false);
                var message = StunMessage.Parse(body.AsSpan(0, size));
                var response = await HandleAsync(message).ConfigureAwait(false);
                if (response is not null)
                {
                    await WriteAsync(response).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Sends bytes to the client.
        /// </summary>
        /// <param name="data">The frame.</param>
        /// <returns>A task that completes when the bytes were written.</returns>
        public async Task WriteAsync(ReadOnlyMemory<byte> data)
        {
            await _writeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await stream.WriteAsync(data).ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }
        }

        /// <summary>
        /// Handles a STUN message.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <returns>The encoded response, or <see langword="null"/> for indications.</returns>
        private async Task<byte[]?> HandleAsync(StunMessage message)
        {
            if (message.Class == Stun.ClassIndication)
            {
                if (message.Method == Stun.Send && Allocation is { } allocation
                    && message.GetXorAddress(Stun.AttrXorPeerAddress) is { } peer && message.Get(Stun.AttrData) is { } data)
                {
                    await allocation.SendAsync(peer, data).ConfigureAwait(false);
                }

                return null;
            }

            if (message.Class != Stun.ClassRequest)
            {
                return null;
            }

            if (message.Method == Stun.Binding)
            {
                return Reply(message).AddXorAddress(Stun.AttrXorMappedAddress, Remote).Encode();
            }

            if (!Authenticate(message, out var challenge))
            {
                return challenge;
            }

            return message.Method switch
            {
                Stun.Allocate => HandleAllocate(message),
                Stun.Refresh => HandleRefresh(message),
                Stun.CreatePermission => HandleCreatePermission(message),
                Stun.ChannelBind => HandleChannelBind(message),
                _ => Error(message, 400, "Bad Request", signed: true),
            };
        }

        /// <summary>
        /// Gets the address of the client.
        /// </summary>
        private IPEndPoint Remote => Normalize((IPEndPoint)client.Client.RemoteEndPoint!);

        /// <summary>
        /// Checks the long-term credentials; answers 401 with the realm and nonce otherwise.
        /// </summary>
        /// <param name="message">The request.</param>
        /// <param name="challenge">The error response when not authenticated.</param>
        /// <returns><see langword="true"/> when the request is authenticated.</returns>
        private bool Authenticate(StunMessage message, out byte[]? challenge)
        {
            challenge = null;
            var username = message.GetText(Stun.AttrUsername);
            if (message.IntegrityOffset < 0 || username is null)
            {
                challenge = Challenge(message, 401, "Unauthorized");
                return false;
            }

            if (message.GetText(Stun.AttrNonce) != nonce || message.GetText(Stun.AttrRealm) != server._options.Realm)
            {
                challenge = Challenge(message, 438, "Stale Nonce");
                return false;
            }

            if (!CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(username),
                    System.Text.Encoding.UTF8.GetBytes(server._options.Credentials.Username))
                || !message.VerifyIntegrity(server._key))
            {
                server._logger.LogWarning("TURN request from {Client} with wrong credentials", Remote);
                challenge = Challenge(message, 401, "Unauthorized");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Creates the relay.
        /// </summary>
        /// <param name="message">The request.</param>
        /// <returns>The response.</returns>
        private byte[] HandleAllocate(StunMessage message)
        {
            if (Allocation is not null)
            {
                return Error(message, 437, "Allocation Mismatch", signed: true);
            }

            if (message.Get(Stun.AttrRequestedTransport) is not [17, 0, 0, 0])
            {
                return Error(message, 442, "Unsupported Transport Protocol", signed: true);
            }

            if (server._allocations.Count >= server._options.MaxAllocations)
            {
                return Error(message, 486, "Allocation Quota Reached", signed: true);
            }

            var local = Normalize((IPEndPoint)client.Client.LocalEndPoint!);
            var allocation = new Allocation(this, local.Address, server._timeProvider, server._logger);
            allocation.Refresh(Lifetime(message));
            Allocation = allocation;
            server._allocations[allocation] = 0;
            allocation.Start();
            server._logger.LogInformation("TURN relay {Relay} for {Client}", allocation.RelayAddress, Remote);
            return Reply(message)
                .AddXorAddress(Stun.AttrXorRelayedAddress, allocation.RelayAddress)
                .AddXorAddress(Stun.AttrXorMappedAddress, Remote)
                .AddUInt32(Stun.AttrLifetime, (uint)allocation.Lifetime.TotalSeconds)
                .Encode(server._key);
        }

        /// <summary>
        /// Extends or deletes the relay.
        /// </summary>
        /// <param name="message">The request.</param>
        /// <returns>The response.</returns>
        private byte[] HandleRefresh(StunMessage message)
        {
            if (Allocation is not { } allocation)
            {
                return Error(message, 437, "Allocation Mismatch", signed: true);
            }

            var lifetime = Lifetime(message);
            allocation.Refresh(lifetime);
            if (lifetime == TimeSpan.Zero)
            {
                server._allocations.TryRemove(allocation, out _);
                allocation.Dispose();
                Allocation = null;
            }

            return Reply(message).AddUInt32(Stun.AttrLifetime, (uint)lifetime.TotalSeconds).Encode(server._key);
        }

        /// <summary>
        /// Installs permissions.
        /// </summary>
        /// <param name="message">The request.</param>
        /// <returns>The response.</returns>
        private byte[] HandleCreatePermission(StunMessage message)
        {
            if (Allocation is not { } allocation)
            {
                return Error(message, 437, "Allocation Mismatch", signed: true);
            }

            var peers = message.GetAll(Stun.AttrXorPeerAddress).Select(v => StunMessage.DecodeXorAddress(v, message.TransactionId)).ToArray();
            if (peers.Length == 0 || peers.Any(p => p is null))
            {
                return Error(message, 400, "Bad Request", signed: true);
            }

            foreach (var peer in peers)
            {
                allocation.Permit(peer!.Address);
            }

            return Reply(message).Encode(server._key);
        }

        /// <summary>
        /// Binds a channel to a peer.
        /// </summary>
        /// <param name="message">The request.</param>
        /// <returns>The response.</returns>
        private byte[] HandleChannelBind(StunMessage message)
        {
            if (Allocation is not { } allocation)
            {
                return Error(message, 437, "Allocation Mismatch", signed: true);
            }

            var number = message.Get(Stun.AttrChannelNumber) is { Length: 4 } raw ? BinaryPrimitives.ReadUInt16BigEndian(raw) : (ushort)0;
            var peer = message.GetXorAddress(Stun.AttrXorPeerAddress);
            if (number is < Stun.FirstChannel or > Stun.LastChannel || peer is null || !allocation.Bind(number, peer))
            {
                return Error(message, 400, "Bad Request", signed: true);
            }

            return Reply(message).Encode(server._key);
        }

        /// <summary>
        /// Reads the requested lifetime, limited by the server.
        /// </summary>
        /// <param name="message">The request.</param>
        /// <returns>The lifetime.</returns>
        private TimeSpan Lifetime(StunMessage message)
        {
            var requested = message.Get(Stun.AttrLifetime) is { Length: 4 } raw
                ? TimeSpan.FromSeconds(BinaryPrimitives.ReadUInt32BigEndian(raw))
                : TimeSpan.FromMinutes(10);
            return requested > server._options.MaxLifetime ? server._options.MaxLifetime : requested;
        }

        /// <summary>
        /// Creates a success response.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>The response.</returns>
        private static StunMessage Reply(StunMessage request) => new(request.Method, Stun.ClassSuccess, request.TransactionId);

        /// <summary>
        /// Creates an error response.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="code">The error code.</param>
        /// <param name="reason">The reason.</param>
        /// <param name="signed">Whether to add MESSAGE-INTEGRITY.</param>
        /// <returns>The encoded response.</returns>
        private byte[] Error(StunMessage request, int code, string reason, bool signed)
            => new StunMessage(request.Method, Stun.ClassError, request.TransactionId)
                .AddError(code, reason)
                .Encode(signed ? server._key : null);

        /// <summary>
        /// Creates an authentication challenge.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="code">401 or 438.</param>
        /// <param name="reason">The reason.</param>
        /// <returns>The encoded response.</returns>
        private byte[] Challenge(StunMessage request, int code, string reason)
            => new StunMessage(request.Method, Stun.ClassError, request.TransactionId)
                .AddError(code, reason)
                .AddText(Stun.AttrRealm, server._options.Realm)
                .AddText(Stun.AttrNonce, nonce)
                .Encode();
    }

    /// <summary>
    /// A UDP relay of one client.
    /// </summary>
    private sealed class Allocation : IDisposable
    {
        /// <summary>
        /// The client.
        /// </summary>
        private readonly ClientSession _owner;

        /// <summary>
        /// The relay socket.
        /// </summary>
        private readonly UdpClient _udp;

        /// <summary>
        /// The time provider.
        /// </summary>
        private readonly TimeProvider _timeProvider;

        /// <summary>
        /// The logger.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Permitted peer addresses and their expiry.
        /// </summary>
        private readonly ConcurrentDictionary<IPAddress, DateTimeOffset> _permissions = new();

        /// <summary>
        /// Channels by number.
        /// </summary>
        private readonly ConcurrentDictionary<ushort, IPEndPoint> _channels = new();

        /// <summary>
        /// Channel numbers by peer.
        /// </summary>
        private readonly ConcurrentDictionary<IPEndPoint, ushort> _channelsByPeer = new();

        /// <summary>
        /// Stops the receive loop.
        /// </summary>
        private readonly CancellationTokenSource _stop = new();

        /// <summary>
        /// The expiry of the allocation.
        /// </summary>
        private DateTimeOffset _expires;

        /// <summary>
        /// Initializes a new instance of the <see cref="Allocation"/> class.
        /// </summary>
        /// <param name="owner">The client.</param>
        /// <param name="address">The local address the client connected to; the relay uses it.</param>
        /// <param name="timeProvider">The time provider.</param>
        /// <param name="logger">The logger.</param>
        public Allocation(ClientSession owner, IPAddress address, TimeProvider timeProvider, ILogger logger)
        {
            _owner = owner;
            _timeProvider = timeProvider;
            _logger = logger;
            _udp = new UdpClient(new IPEndPoint(address.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0));
            RelayAddress = new IPEndPoint(address, ((IPEndPoint)_udp.Client.LocalEndPoint!).Port);
        }

        /// <summary>
        /// Gets the relayed transport address.
        /// </summary>
        public IPEndPoint RelayAddress { get; }

        /// <summary>
        /// Gets the granted lifetime.
        /// </summary>
        public TimeSpan Lifetime { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the allocation expired.
        /// </summary>
        public bool IsExpired => _timeProvider.GetUtcNow() > _expires;

        /// <summary>
        /// Starts relaying peer packets to the client.
        /// </summary>
        public void Start() => _ = Task.Run(ReceiveLoopAsync);

        /// <summary>
        /// Sets the lifetime from now.
        /// </summary>
        /// <param name="lifetime">The lifetime.</param>
        public void Refresh(TimeSpan lifetime)
        {
            Lifetime = lifetime;
            _expires = _timeProvider.GetUtcNow() + lifetime;
        }

        /// <summary>
        /// Allows packets to and from an address.
        /// </summary>
        /// <param name="peer">The peer address.</param>
        public void Permit(IPAddress peer) => _permissions[Normalize(peer)] = _timeProvider.GetUtcNow() + PermissionLifetime;

        /// <summary>
        /// Binds a channel; a channel stays with its first peer.
        /// </summary>
        /// <param name="number">The channel number.</param>
        /// <param name="peer">The peer.</param>
        /// <returns><see langword="false"/> when the number or the peer is bound differently.</returns>
        public bool Bind(ushort number, IPEndPoint peer)
        {
            peer = Normalize(peer);
            if ((_channels.TryGetValue(number, out var existing) && !existing.Equals(peer))
                || (_channelsByPeer.TryGetValue(peer, out var existingNumber) && existingNumber != number))
            {
                return false;
            }

            _channels[number] = peer;
            _channelsByPeer[peer] = number;
            Permit(peer.Address);
            return true;
        }

        /// <summary>
        /// Sends a packet from a Send indication.
        /// </summary>
        /// <param name="peer">The peer.</param>
        /// <param name="data">The payload.</param>
        /// <returns>A task that completes when the packet was sent.</returns>
        public async ValueTask SendAsync(IPEndPoint peer, ReadOnlyMemory<byte> data)
        {
            peer = Normalize(peer);
            if (IsPermitted(peer.Address) && !IsExpired)
            {
                await _udp.SendAsync(data, ToSocket(peer)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Sends a packet from ChannelData.
        /// </summary>
        /// <param name="number">The channel.</param>
        /// <param name="data">The payload.</param>
        /// <returns>A task that completes when the packet was sent.</returns>
        public ValueTask SendToChannelAsync(ushort number, ReadOnlyMemory<byte> data)
            => _channels.TryGetValue(number, out var peer) ? SendAsync(peer, data) : ValueTask.CompletedTask;

        /// <inheritdoc />
        public void Dispose()
        {
            _stop.Cancel();
            _udp.Dispose();
        }

        /// <summary>
        /// Forwards packets of permitted peers to the client.
        /// </summary>
        /// <returns>A task that completes when the relay closes.</returns>
        private async Task ReceiveLoopAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested && !IsExpired)
                {
                    var packet = await _udp.ReceiveAsync(_stop.Token).ConfigureAwait(false);
                    var peer = Normalize(packet.RemoteEndPoint);
                    if (!IsPermitted(peer.Address) || packet.Buffer.Length > StunMessage.MaxSize - 36)
                    {
                        continue;
                    }

                    await _owner.WriteAsync(Frame(peer, packet.Buffer)).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException or IOException)
            {
                _logger.LogDebug("TURN relay {Relay} closed: {Reason}", RelayAddress, ex.Message);
            }
        }

        /// <summary>
        /// Wraps a peer packet as ChannelData or a Data indication.
        /// </summary>
        /// <param name="peer">The peer.</param>
        /// <param name="payload">The packet.</param>
        /// <returns>The frame.</returns>
        private byte[] Frame(IPEndPoint peer, byte[] payload)
        {
            if (_channelsByPeer.TryGetValue(peer, out var number))
            {
                var frame = new byte[4 + ((payload.Length + 3) & ~3)];
                BinaryPrimitives.WriteUInt16BigEndian(frame, number);
                BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), (ushort)payload.Length);
                payload.CopyTo(frame, 4);
                return frame;
            }

            return new StunMessage(Stun.Data, Stun.ClassIndication, RandomNumberGenerator.GetBytes(12))
                .AddXorAddress(Stun.AttrXorPeerAddress, peer)
                .Add(Stun.AttrData, payload)
                .Encode();
        }

        /// <summary>
        /// Checks a permission.
        /// </summary>
        /// <param name="peer">The peer address.</param>
        /// <returns><see langword="true"/> when packets are allowed.</returns>
        private bool IsPermitted(IPAddress peer)
            => _permissions.TryGetValue(peer, out var expires) && expires > _timeProvider.GetUtcNow();

        /// <summary>
        /// Converts an endpoint to the family of the relay socket.
        /// </summary>
        /// <param name="peer">The peer.</param>
        /// <returns>The socket address.</returns>
        private IPEndPoint ToSocket(IPEndPoint peer)
            => _udp.Client.AddressFamily == AddressFamily.InterNetworkV6 && peer.AddressFamily == AddressFamily.InterNetwork
                ? new IPEndPoint(peer.Address.MapToIPv6(), peer.Port)
                : peer;
    }

    /// <summary>
    /// Removes IPv4 mapping from an endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <returns>The plain endpoint.</returns>
    private static IPEndPoint Normalize(IPEndPoint endpoint)
        => endpoint.Address.IsIPv4MappedToIPv6 ? new IPEndPoint(endpoint.Address.MapToIPv4(), endpoint.Port) : endpoint;

    /// <summary>
    /// Removes IPv4 mapping from an address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>The plain address.</returns>
    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
