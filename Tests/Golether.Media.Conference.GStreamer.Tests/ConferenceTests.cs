using System.Collections.Concurrent;
using Golether.Components.Catalog;
using Golether.Components.GStreamer;
using Golether.Core.Data.Enums;
using Golether.Core.Identity;
using Golether.Core.Media;
using Golether.Media.Streaming.Caching;
using Golether.Media.Streaming.Sharing;
using Golether.Transports.Relay;

namespace Golether.Media.Conference.GStreamer.Tests;

/// <summary>
/// End-to-end WebRTC tests: two conference instances in one process exchange test patterns over loopback.
/// </summary>
public sealed class ConferenceTests
{
    /// <summary>
    /// The first participant.
    /// </summary>
    private static readonly PeerId Alice = PeerId.Parse(new string('1', 64));

    /// <summary>
    /// The second participant.
    /// </summary>
    private static readonly PeerId Bob = PeerId.Parse(new string('2', 64));

    /// <summary>
    /// The shared media instances (GStreamer is initialized once per process).
    /// </summary>
    private static readonly Lazy<(GStreamerConferenceMedia? A, GStreamerConferenceMedia? B, string? Skip)> Pair = new(() => CreatePair(relayOnly: false));

    /// <summary>
    /// Two media instances that may connect only through a TURN relay.
    /// </summary>
    private static readonly Lazy<(GStreamerConferenceMedia? A, GStreamerConferenceMedia? B, string? Skip)> RelayPair = new(() => CreatePair(relayOnly: true));

    /// <summary>
    /// Both sides negotiate and receive the other's camera; the preview works.
    /// </summary>
    [Fact]
    public async Task TwoPeers_ExchangeVideo()
    {
        var (a, b, skip) = Pair.Value;
        Assert.SkipWhen(skip is not null, skip ?? string.Empty);
        var token = TestContext.Current.CancellationToken;

        var framesAtA = new ConcurrentDictionary<PeerId, VideoFrame>();
        var framesAtB = new ConcurrentDictionary<PeerId, VideoFrame>();
        a!.VideoFrameReceived += (_, f) => framesAtA[f.PeerId] = f.Frame;
        b!.VideoFrameReceived += (_, f) => framesAtB[f.PeerId] = f.Frame;
        var errors = new ConcurrentQueue<Exception>();
        var speakingAtA = new ConcurrentQueue<SpeakingChange>();
        a.SpeakingChanged += (_, change) => speakingAtA.Enqueue(change);
        a.SignalReady += (_, s) => Track(b.HandleSignalAsync(s with { PeerId = Alice }, token), errors, s.Payload);
        b.SignalReady += (_, s) => Track(a.HandleSignalAsync(s with { PeerId = Bob }, token), errors, s.Payload);

        await a.StartCaptureAsync(null, null, token);
        await b.StartCaptureAsync(null, null, token);
        await a.ConnectAsync(Bob, isOfferer: true, token);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!(framesAtA.ContainsKey(Bob) && framesAtB.ContainsKey(Alice)) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200, token);
        }

        Assert.Empty(errors);
        Assert.True(framesAtA.ContainsKey(default), "Alice sees her own preview.");
        Assert.True(framesAtA.TryGetValue(Bob, out var fromBob), "Alice receives Bob's camera.");
        Assert.True(framesAtB.ContainsKey(Alice), "Bob receives Alice's camera.");
        Assert.Equal(640, fromBob.Width);
        Assert.Equal(360, fromBob.Height);
        Assert.Equal(640 * 360 * 4, fromBob.Pixels.Length);
        Assert.Contains(fromBob.Pixels.ToArray(), value => value != 0);
        Assert.Equal(1, a.PeerCount);
        Assert.Equal(1, b.PeerCount);

        // The report must be able to say what a connection really carries: both directions, on both sides.
        var linkAtA = Assert.Single(a.GetPeerDiagnostics());
        Assert.Equal((Bob, true, VideoQuality.High), (linkAtA.Peer, linkAtA.Verified, linkAtA.Quality));
        Assert.True(linkAtA.VideoSent > 0, "Алиса отдаёт кадры камеры.");
        Assert.True(linkAtA.VideoReceived > 0, "Алиса принимает кадры камеры.");
        Assert.True(linkAtA.AudioSent > 0, "Алиса отдаёт голос.");
        var linkAtB = Assert.Single(b.GetPeerDiagnostics());
        Assert.True(linkAtB.VideoSent > 0 && linkAtB.VideoReceived > 0, "Боб отдаёт и принимает кадры камеры.");

        // The steady test tone counts as speech: Alice sees herself and Bob speaking.
        for (var i = 0; i < 50 && !(speakingAtA.Contains(new SpeakingChange(Bob, true)) && speakingAtA.Contains(new SpeakingChange(default, true))); i++)
        {
            await Task.Delay(100, token);
        }

        Assert.Contains(new SpeakingChange(default, true), speakingAtA);
        Assert.Contains(new SpeakingChange(Bob, true), speakingAtA);

        // The data channel opens on both sides and carries messages in order.
        var atB = new ConcurrentQueue<(PeerId Peer, byte[] Data)>();
        b.DataReceived += (_, peer, data) => atB.Enqueue((peer, data.ToArray()));
        for (var i = 0; i < 100 && !(a.DataPeers.Contains(Bob) && b.DataPeers.Contains(Alice)); i++)
        {
            await Task.Delay(100, token);
        }

        Assert.Contains(Bob, a.DataPeers);
        Assert.Contains(Alice, b.DataPeers);
        var big = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16 * 1024 + 13);
        Assert.True(a.TrySendData(Bob, [1, 2, 3]));
        Assert.True(a.TrySendData(Bob, big));
        Assert.False(a.TrySendData(PeerId.Parse(new string('7', 64)), [1]));
        for (var i = 0; i < 50 && atB.Count < 2; i++)
        {
            await Task.Delay(100, token);
        }

        Assert.Equal([(Alice, new byte[] { 1, 2, 3 }), (Alice, big)], atB.Select(m => (m.Peer, m.Data)).ToArray(), MessageComparer.Instance);

        await a.DisconnectAsync(Bob);
        Assert.Equal(0, a.PeerCount);
    }

    /// <summary>
    /// A participant switched to the economy stream receives 320×180 frames and returns to 640×360; the connection
    /// statistics can be read.
    /// </summary>
    [Fact]
    public async Task EconomyStream_SwitchesResolution()
    {
        var (a, b, skip) = Pair.Value;
        Assert.SkipWhen(skip is not null, skip ?? string.Empty);
        var token = TestContext.Current.CancellationToken;
        var gina = PeerId.Parse(new string('9', 64));
        var hank = PeerId.Parse(new string('8', 64));
        var sizes = new ConcurrentQueue<(int Width, int Height)>();
        b!.VideoFrameReceived += (_, f) =>
        {
            if (f.PeerId == gina)
            {
                sizes.Enqueue((f.Frame.Width, f.Frame.Height));
            }
        };
        a!.SignalReady += (_, s) => { if (s.PeerId == hank) { _ = b.HandleSignalAsync(s with { PeerId = gina }, token); } };
        b.SignalReady += (_, s) => { if (s.PeerId == gina) { _ = a.HandleSignalAsync(s with { PeerId = hank }, token); } };
        await a.StartCaptureAsync(null, null, token);
        await b.StartCaptureAsync(null, null, token);
        await a.ConnectAsync(hank, isOfferer: true, token);

        await WaitForSize(640, 360);
        a.ForceVideoQuality(hank, VideoQuality.Low);
        await WaitForSize(320, 180);
        a.ForceVideoQuality(hank, VideoQuality.High);
        await WaitForSize(640, 360);

        // Receiver reports arrive every few seconds; the round trip on loopback is short and nothing is lost.
        (double? Loss, double? RoundTrip) stats = (null, null);
        for (var i = 0; i < 40 && stats.RoundTrip is null; i++)
        {
            await Task.Delay(250, token);
            stats = a.ReadNetworkStats(hank);
        }

        Assert.NotNull(stats.RoundTrip);
        Assert.InRange(stats.RoundTrip.Value, 0, 0.2);
        Assert.InRange(stats.Loss ?? 0, 0, 0.01);
        a.UpdateVideoQuality();
        Assert.Equal((null, null), a.ReadNetworkStats(PeerId.Parse(new string('6', 64))));
        await a.DisconnectAsync(hank);

        async Task WaitForSize(int width, int height)
        {
            sizes.Clear();
            for (var i = 0; i < 150 && !sizes.Contains((width, height)); i++)
            {
                await Task.Delay(100, token);
            }

            Assert.Contains((width, height), sizes);
        }
    }

    /// <summary>
    /// A DTLS certificate that does not match the fingerprint in the signaled SDP (a man in the middle on the media
    /// path) gets no media.
    /// </summary>
    [Fact]
    public async Task ForgedFingerprint_GetsNoMedia()
    {
        var (a, b, skip) = Pair.Value;
        Assert.SkipWhen(skip is not null, skip ?? string.Empty);
        var token = TestContext.Current.CancellationToken;
        var carol = PeerId.Parse(new string('3', 64));
        var dave = PeerId.Parse(new string('4', 64));

        var framesAtB = new ConcurrentDictionary<PeerId, VideoFrame>();
        var framesAtA = new ConcurrentDictionary<PeerId, VideoFrame>();
        a!.VideoFrameReceived += (_, f) => framesAtA[f.PeerId] = f.Frame;
        b!.VideoFrameReceived += (_, f) => framesAtB[f.PeerId] = f.Frame;

        // Only the signals between Carol (at A) and Dave (at B) are forwarded; the offer carries a foreign fingerprint.
        a.SignalReady += (_, s) =>
        {
            if (s.PeerId == dave)
            {
                var payload = s.Kind == "offer" ? ForgeFingerprint(s.Payload) : s.Payload;
                _ = b.HandleSignalAsync(new ConferenceSignal(carol, s.Kind, payload), token);
            }
        };
        b.SignalReady += (_, s) =>
        {
            if (s.PeerId == carol)
            {
                _ = a.HandleSignalAsync(new ConferenceSignal(dave, s.Kind, s.Payload), token);
            }
        };

        await a.StartCaptureAsync(null, null, token);
        await b.StartCaptureAsync(null, null, token);
        await a.ConnectAsync(dave, isOfferer: true, token);
        await Task.Delay(TimeSpan.FromSeconds(12), token);

        Assert.False(framesAtB.ContainsKey(carol), "Media must not flow when the DTLS fingerprint does not match.");
        Assert.False(framesAtA.ContainsKey(dave), "Media must not flow when the DTLS fingerprint does not match.");
        await a.DisconnectAsync(dave);
        await b.DisconnectAsync(carol);
    }

    /// <summary>
    /// With direct paths forbidden, both sides reach each other through the host's TURN relay over TCP.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task RelayOnly_ExchangesVideoThroughTurn()
    {
        var (a, b, skip) = RelayPair.Value;
        Assert.SkipWhen(skip is not null, skip ?? string.Empty);
        var token = TestContext.Current.CancellationToken;
        await using var relay = new TurnServer(new TurnServerOptions { Credentials = RelayCredentials.CreateRandom(0), ListenAddress = System.Net.IPAddress.Loopback });
        relay.Start();
        var uri = (RelayCredentials.CreateRandom(0) with { Port = relay.Port }).ToTurnUri("127.0.0.1");
        Assert.Throws<ArgumentException>(() => a!.SetRelay("http://example.org"));

        // Wrong credentials: no relay, no media.
        var carol = PeerId.Parse(new string('5', 64));
        var dave = PeerId.Parse(new string('6', 64));
        var framesAtA = new ConcurrentDictionary<PeerId, VideoFrame>();
        var framesAtB = new ConcurrentDictionary<PeerId, VideoFrame>();
        a!.VideoFrameReceived += (_, f) => framesAtA[f.PeerId] = f.Frame;
        b!.VideoFrameReceived += (_, f) => framesAtB[f.PeerId] = f.Frame;
        var errors = new ConcurrentQueue<Exception>();
        a.SignalReady += (_, s) => Track(b.HandleSignalAsync(s with { PeerId = s.PeerId == dave ? carol : Alice }, token), errors, s.Payload);
        b.SignalReady += (_, s) => Track(a.HandleSignalAsync(s with { PeerId = s.PeerId == carol ? dave : Bob }, token), errors, s.Payload);
        a.SetRelay(uri);
        b.SetRelay(uri);
        await a.StartCaptureAsync(null, null, token);
        await b.StartCaptureAsync(null, null, token);
        await a.ConnectAsync(dave, isOfferer: true, token);
        await Task.Delay(TimeSpan.FromSeconds(6), token);
        Assert.False(framesAtB.ContainsKey(carol), "No media without the session credentials.");
        Assert.Equal(0, relay.AllocationCount);
        await a.DisconnectAsync(dave);
        await b.DisconnectAsync(carol);

        // The session credentials: media flows through the relay.
        await using var sessionRelay = new TurnServer(new TurnServerOptions { Credentials = RelayCredentials.CreateRandom(0), ListenAddress = System.Net.IPAddress.Loopback });
        sessionRelay.Start();
        var credentials = sessionRelay.Credentials;
        a.SetRelay(credentials.ToTurnUri("127.0.0.1"));
        b.SetRelay(credentials.ToTurnUri("127.0.0.1"));
        await a.ConnectAsync(Bob, isOfferer: true, token);

        var deadline = DateTime.UtcNow.AddSeconds(40);
        while (!(framesAtA.ContainsKey(Bob) && framesAtB.ContainsKey(Alice)) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200, token);
        }

        Assert.Empty(errors);
        Assert.True(framesAtA.ContainsKey(Bob), "Alice receives Bob's camera through the relay.");
        Assert.True(framesAtB.ContainsKey(Alice), "Bob receives Alice's camera through the relay.");
        Assert.True(sessionRelay.AllocationCount >= 2, $"Both sides allocate a relay ({sessionRelay.AllocationCount}).");
        await a.DisconnectAsync(Bob);
        await b.DisconnectAsync(Alice);
    }

    /// <summary>
    /// A 4 MiB chunk travels between two participants over the real data channel.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task DataChannel_TransfersChunk()
    {
        var (a, b, skip) = Pair.Value;
        Assert.SkipWhen(skip is not null, skip ?? string.Empty);
        var token = TestContext.Current.CancellationToken;
        var erin = PeerId.Parse(new string('e', 64));
        var frank = PeerId.Parse(new string('f', 64));
        a!.SignalReady += (_, s) => { if (s.PeerId == frank) { _ = b!.HandleSignalAsync(s with { PeerId = erin }, token); } };
        b!.SignalReady += (_, s) => { if (s.PeerId == erin) { _ = a.HandleSignalAsync(s with { PeerId = frank }, token); } };
        await a.StartCaptureAsync(null, null, token);
        await b.StartCaptureAsync(null, null, token);
        await a.ConnectAsync(frank, isOfferer: true, token);
        for (var i = 0; i < 150 && !(a.DataPeers.Contains(frank) && b.DataPeers.Contains(erin)); i++)
        {
            await Task.Delay(100, token);
        }

        var content = System.Security.Cryptography.RandomNumberGenerator.GetBytes(4 * 1024 * 1024);
        var media = new MediaDescriptor { FileName = "chunk.bin", Length = content.Length, QuickId = new string('d', 64) };
        var cache = new ChunkCache();
        cache.Add(0, content);
        using var erinTransport = new Transport(a);
        using var frankTransport = new Transport(b);
        await using var sender = new PeerChunkExchange(media, erinTransport, cache.Peek, cache.GetRanges);
        await using var receiver = new PeerChunkExchange(media, frankTransport, _ => null, _ => []);
        sender.Announce();
        for (var i = 0; i < 50 && receiver.PeersHaving(0).Count == 0; i++)
        {
            await Task.Delay(100, token);
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var received = await receiver.TryFetchAsync(0, token);
        clock.Stop();

        Assert.NotNull(received);
        Assert.Equal(erin, received.Value.Peer);
        Assert.True(content.AsSpan().SequenceEqual(received.Value.Data), "The chunk arrives intact.");
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"4 MiB took {clock.Elapsed}.");
        TestContext.Current.SendDiagnosticMessage($"4 MiB over the data channel: {clock.Elapsed.TotalMilliseconds:0} ms");
        await a.DisconnectAsync(frank);
        await b.DisconnectAsync(erin);
    }

    /// <summary>
    /// <see cref="IPeerMessageTransport"/> over a conference backend.
    /// </summary>
    private sealed class Transport : IPeerMessageTransport, IDisposable
    {
        /// <summary>
        /// The backend.
        /// </summary>
        private readonly GStreamerConferenceMedia _media;

        /// <summary>
        /// Initializes a new instance of the <see cref="Transport"/> class.
        /// </summary>
        /// <param name="media">The backend.</param>
        public Transport(GStreamerConferenceMedia media)
        {
            _media = media;
            _media.DataReceived += OnData;
        }

        /// <inheritdoc />
        public event EventHandler<PeerMessage>? MessageReceived;

        /// <inheritdoc />
        public IReadOnlyCollection<PeerId> ConnectedPeers => _media.DataPeers;

        /// <inheritdoc />
        public bool TrySend(PeerId peer, ReadOnlySpan<byte> message) => _media.TrySendData(peer, message);

        /// <inheritdoc />
        public void Dispose() => _media.DataReceived -= OnData;

        /// <summary>
        /// Passes a message on.
        /// </summary>
        /// <param name="sender">The backend.</param>
        /// <param name="peer">The sender.</param>
        /// <param name="data">The message.</param>
        private void OnData(object? sender, PeerId peer, ReadOnlySpan<byte> data) => MessageReceived?.Invoke(this, new PeerMessage(peer, data.ToArray()));
    }

    /// <summary>
    /// Compares received messages by sender and content.
    /// </summary>
    private sealed class MessageComparer : IEqualityComparer<(PeerId Peer, byte[] Data)>
    {
        /// <summary>
        /// The shared instance.
        /// </summary>
        public static readonly MessageComparer Instance = new();

        /// <inheritdoc />
        public bool Equals((PeerId Peer, byte[] Data) x, (PeerId Peer, byte[] Data) y) => x.Peer == y.Peer && x.Data.AsSpan().SequenceEqual(y.Data);

        /// <inheritdoc />
        public int GetHashCode((PeerId Peer, byte[] Data) obj) => obj.Peer.GetHashCode();
    }

    /// <summary>
    /// Records the failure of a signaling task.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="errors">The failures.</param>
    /// <param name="payload">The signaling payload, reported with the failure.</param>
    private static void Track(Task task, ConcurrentQueue<Exception> errors, string payload)
        => task.ContinueWith(
            t => errors.Enqueue(new InvalidOperationException($"Signal [{Escape(payload)}] failed.", t.Exception!.GetBaseException())),
            TaskContinuationOptions.OnlyOnFaulted);

    /// <summary>
    /// Makes line breaks visible.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The escaped text.</returns>
    private static string Escape(string text) => text.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    /// <summary>
    /// Replaces every SHA-256 fingerprint in an SDP with a different one.
    /// </summary>
    /// <param name="sdp">The SDP.</param>
    /// <returns>The forged SDP.</returns>
    private static string ForgeFingerprint(string sdp)
    {
        var forged = string.Join(':', Enumerable.Repeat("AB", 32));
        var result = System.Text.RegularExpressions.Regex.Replace(sdp, @"(a=fingerprint:sha-256 )[0-9A-Fa-f:]+", "${1}" + forged);
        Assert.NotEqual(sdp, result);
        return result;
    }

    /// <summary>
    /// Invalid signaling is rejected without affecting the media.
    /// </summary>
    [Fact]
    public async Task InvalidSignals_AreRejected()
    {
        var (a, _, skip) = Pair.Value;
        Assert.SkipWhen(skip is not null, skip ?? string.Empty);
        var stranger = PeerId.Parse(new string('9', 64));
        var token = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<FormatException>(() => a!.HandleSignalAsync(new ConferenceSignal(stranger, "offer", "not sdp"), token));
        await Assert.ThrowsAsync<FormatException>(() => a!.HandleSignalAsync(new ConferenceSignal(stranger, "candidate", "x"), token));
        await Assert.ThrowsAsync<FormatException>(() => a!.HandleSignalAsync(new ConferenceSignal(stranger, "weird", "x"), token));
        await a!.DisconnectAsync(stranger);
    }

    /// <summary>
    /// Creates the two media instances with test sources.
    /// </summary>
    /// <returns>The instances or the reason to skip.</returns>
    /// <param name="relayOnly">Whether only relayed paths are allowed.</param>
    private static (GStreamerConferenceMedia?, GStreamerConferenceMedia?, string?) CreatePair(bool relayOnly)
    {
        var bundle = FindBundle();
        if (bundle is null && OperatingSystem.IsWindows())
        {
            return (null, null, "The GStreamer bundle is not prepared (run golether-components fetch).");
        }

        var registry = Path.Combine(Path.GetTempPath(), "golether-tests", "gst-registry.bin");
        var debugLog = Environment.GetEnvironmentVariable("GOLETHER_TEST_GST_LOG");
        if (!string.IsNullOrEmpty(debugLog))
        {
            Environment.SetEnvironmentVariable("GST_DEBUG", "2,webrtcbin:5,webrtcbin*:4,nice*:4,dtls*:4,appsrc:4");
            Environment.SetEnvironmentVariable("GST_DEBUG_FILE", debugLog);
            Environment.SetEnvironmentVariable("GST_DEBUG_NO_COLOR", "1");
        }

        GStreamerConferenceMedia Create() => new(
            new GStreamerConferenceOptions
            {
                BundleRoot = bundle,
                RegistryFile = registry,
                UseTestSources = true,
                EnablePlayback = false,
                RelayOnly = relayOnly,
            },
            new FileLogger<GStreamerConferenceMedia>(debugLog is { Length: > 0 } ? debugLog + ".app.log" : null));

        var a = Create();
        if (!a.IsAvailable)
        {
            return (null, null, a.UnavailableReason);
        }

        return (a, Create(), null);
    }

    /// <summary>
    /// Finds the bundle prepared in the repository.
    /// </summary>
    /// <returns>The bundle root or <see langword="null"/>.</returns>
    private static string? FindBundle()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Native", ComponentCatalog.CurrentRuntimeIdentifier, "gstreamer");
            if (GStreamerBundle.IsComplete(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
