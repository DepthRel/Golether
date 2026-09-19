using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Golether.Core.Identity;
using Golether.Media.Conference.GStreamer.Native;
using Microsoft.Extensions.Logging;

namespace Golether.Media.Conference.GStreamer;

/// <summary>
/// The signaling kinds exchanged between peers.
/// </summary>
internal static class SignalKinds
{
    /// <summary>
    /// An SDP offer.
    /// </summary>
    public const string Offer = "offer";

    /// <summary>
    /// An SDP answer.
    /// </summary>
    public const string Answer = "answer";

    /// <summary>
    /// An ICE candidate: <c>&lt;m-line index&gt;\n&lt;candidate&gt;</c>.
    /// </summary>
    public const string Candidate = "candidate";
}

/// <summary>
/// The WebRTC connection with one remote participant: its own pipeline with <c>webrtcbin</c>, fed with the
/// already encoded camera and microphone, and decoding what the peer sends.
/// </summary>
/// <remarks>
/// webrtcbin does not compare the DTLS certificate of the remote side with the fingerprint in its SDP. The peer does it
/// itself: no media is sent to or accepted from the remote side until the certificate matches the fingerprint that
/// arrived over the authenticated session channel; a mismatch disconnects the peer.
/// </remarks>
internal sealed partial class WebRtcPeer : IDisposable
{
    /// <summary>
    /// The label of the data channel for chunk sharing.
    /// </summary>
    private const string DataChannelLabel = "golether";

    /// <summary>
    /// <c>GST_WEBRTC_DATA_CHANNEL_STATE_OPEN</c>.
    /// </summary>
    private const int DataChannelOpen = 2;

    /// <summary>
    /// The largest amount of unsent data before sends are refused.
    /// </summary>
    private const ulong MaxBufferedData = 1024 * 1024;

    /// <summary>
    /// <c>GST_WEBRTC_DTLS_TRANSPORT_STATE_CONNECTED</c>.
    /// </summary>
    private const int DtlsConnected = 4;

    /// <summary>
    /// <c>GST_WEBRTC_DTLS_TRANSPORT_STATE_FAILED</c>.
    /// </summary>
    private const int DtlsFailed = 2;

    /// <summary>
    /// How often the DTLS state is checked.
    /// </summary>
    private static readonly TimeSpan VerificationInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Guards native calls of the verification against disposal.
    /// </summary>
    private readonly Lock _nativeGate = new();

    /// <summary>
    /// The SHA-256 fingerprint of the remote DTLS certificate taken from the remote SDP.
    /// </summary>
    private byte[]? _expectedFingerprint;

    /// <summary>
    /// Whether the remote DTLS certificate matched the fingerprint; media flows only then.
    /// </summary>
    private volatile bool _verified;

    /// <summary>
    /// The data channel (a reference is held), or zero.
    /// </summary>
    private nint _channel;

    /// <summary>
    /// Whether the data channel is open.
    /// </summary>
    private volatile bool _channelOpen;

    /// <summary>
    /// The maximum size of a signaling payload.
    /// </summary>
    public const int MaxSignalLength = 64 * 1024;

    /// <summary>
    /// The pipeline of the peer.
    /// </summary>
    private const string Description =
        "webrtcbin name=webrtc bundle-policy=max-bundle latency=300 " +
        "appsrc name=vsrc is-live=true do-timestamp=true format=time ! valve name=vgate drop=true drop-mode=forward-sticky-events ! " +
        "queue max-size-buffers=30 leaky=downstream ! " +
        "rtpvp8pay pt=96 mtu=1200 picture-id-mode=15-bit ! application/x-rtp,media=video,encoding-name=VP8,payload=96 ! webrtc. " +
        "appsrc name=asrc is-live=true do-timestamp=true format=time ! valve name=agate drop=true drop-mode=forward-sticky-events ! " +
        "queue max-size-buffers=50 leaky=downstream ! " +
        "rtpopuspay pt=97 ! application/x-rtp,media=audio,encoding-name=OPUS,payload=97 ! webrtc.";

    /// <summary>
    /// The receiving chain of video: frames keep their size (640×360 or the economy 320×180), larger ones are scaled down.
    /// </summary>
    private const string VideoReceiver =
        "queue ! rtpvp8depay ! vp8dec ! videoconvert ! videoscale ! " +
        "video/x-raw,format=BGRA,width=[16,1280],height=[16,720],pixel-aspect-ratio=1/1 ! " +
        "appsink name=rvideo emit-signals=true max-buffers=1 drop=true sync=false";

    /// <summary>
    /// The receiving chain of audio.
    /// </summary>
    private const string AudioReceiver =
        "queue ! rtpopusdepay ! opusdec plc=true ! audioconvert ! audioresample ! " +
        "audio/x-raw,format=S16LE,rate=48000,channels=1,layout=interleaved ! " +
        "appsink name=raudio emit-signals=true sync=false max-buffers=50 drop=true";

    /// <summary>
    /// The owner.
    /// </summary>
    private readonly IPeerHost _host;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// The pipeline.
    /// </summary>
    private readonly GstPipeline _pipeline;

    /// <summary>
    /// The handle passed to native callbacks.
    /// </summary>
    private readonly GCHandle _self;

    /// <summary>
    /// Receiving bins added for incoming streams (released with the pipeline).
    /// </summary>
    private readonly List<nint> _receivers = [];

    /// <summary>
    /// Serializes signaling operations.
    /// </summary>
    private readonly SemaphoreSlim _signaling = new(1, 1);

    /// <summary>
    /// Whether the caps of the video appsrc were set.
    /// </summary>
    private nint _videoCaps;

    /// <summary>
    /// Whether the caps of the audio appsrc were set.
    /// </summary>
    private nint _audioCaps;

    /// <summary>
    /// Whether the peer is disposed.
    /// </summary>
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebRtcPeer"/> class.
    /// </summary>
    /// <param name="peer">The remote participant.</param>
    /// <param name="isOfferer">Whether this side creates the offer.</param>
    /// <param name="audioSlot">The mixer slot for the voice of the peer.</param>
    /// <param name="host">The owner.</param>
    /// <param name="network">The STUN and TURN servers.</param>
    /// <param name="logger">The logger.</param>
    public unsafe WebRtcPeer(PeerId peer, bool isOfferer, int audioSlot, IPeerHost host, PeerNetwork network, ILogger logger)
    {
        Peer = peer;
        IsOfferer = isOfferer;
        AudioSlot = audioSlot;
        _host = host;
        _logger = logger;
        _pipeline = new GstPipeline($"peer {peer.ToShortString()}", Description, logger);
        _pipeline.Failed += (_, error) => _logger.LogWarning("Peer {Peer}: {Error}", Peer.ToShortString(), error);
        _self = GCHandle.Alloc(this);
        var webrtc = _pipeline.Element("webrtc");
        if (!string.IsNullOrEmpty(network.StunServer))
        {
            Gst.UtilSetObjectArg(webrtc, "stun-server", network.StunServer);
        }

        if (!string.IsNullOrEmpty(network.TurnServer))
        {
            Gst.UtilSetObjectArg(webrtc, "turn-server", network.TurnServer);
        }

        if (network.RelayOnly)
        {
            Gst.UtilSetObjectArg(webrtc, "ice-transport-policy", "relay");
        }

        var data = GCHandle.ToIntPtr(_self);
        Gst.SignalConnectData(webrtc, "on-negotiation-needed", (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnNegotiationNeeded, data, 0, 0);
        Gst.SignalConnectData(webrtc, "on-ice-candidate", (nint)(delegate* unmanaged[Cdecl]<nint, uint, nint, nint, void>)&OnIceCandidate, data, 0, 0);
        Gst.SignalConnectData(webrtc, "pad-added", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnPadAdded, data, 0, 0);
        Gst.SignalConnectData(webrtc, "on-data-channel", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnDataChannel, data, 0, 0);
    }

    /// <summary>
    /// Gets the remote participant.
    /// </summary>
    public PeerId Peer { get; }

    /// <summary>
    /// Gets a value indicating whether the DTLS certificate of the remote side has been verified.
    /// </summary>
    public bool IsVerified => _verified;

    /// <summary>
    /// Camera frames handed to this connection.
    /// </summary>
    private long _videoSent;

    /// <summary>
    /// Voice frames handed to this connection.
    /// </summary>
    private long _audioSent;

    /// <summary>
    /// Camera frames that arrived from the participant.
    /// </summary>
    private long _videoReceived;

    /// <summary>
    /// Voice frames that arrived from the participant.
    /// </summary>
    private long _audioReceived;

    /// <summary>
    /// Returns what this connection has really carried, for the diagnostic report.
    /// </summary>
    /// <returns>The state and the counters.</returns>
    public ConferencePeerDiagnostics Describe() => new(
        Peer,
        _verified,
        IsDataReady,
        VideoQuality,
        Interlocked.Read(ref _videoSent),
        Interlocked.Read(ref _videoReceived),
        Interlocked.Read(ref _audioSent),
        Interlocked.Read(ref _audioReceived));

    /// <summary>
    /// Gets a value indicating whether data can be exchanged: the channel is open and the peer is verified.
    /// </summary>
    public bool IsDataReady => _verified && _channelOpen;

    /// <summary>
    /// Sends a message over the data channel.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns><see langword="false"/> when the channel is not ready or its buffer is full.</returns>
    public unsafe bool TrySendData(ReadOnlySpan<byte> message)
    {
        if (!IsDataReady)
        {
            return false;
        }

        lock (_nativeGate)
        {
            if (_disposed || _channel == 0 || Signals.GetUInt64(_channel, "buffered-amount") > MaxBufferedData)
            {
                return false;
            }

            nint bytes;
            fixed (byte* data = message)
            {
                bytes = Gst.BytesNew(data, (nuint)message.Length);
            }

            try
            {
                Signals.Emit(_channel, "send-data", SignalArg.Boxed(Gst.BytesGetType(), bytes));
                return true;
            }
            finally
            {
                Gst.BytesUnref(bytes);
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether this side creates the offer.
    /// </summary>
    public bool IsOfferer { get; }

    /// <summary>
    /// Gets the mixer slot of the voice.
    /// </summary>
    public int AudioSlot { get; }

    /// <summary>
    /// Checks the shape of signaling data before any native object is created for it.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="payload">The payload.</param>
    /// <exception cref="FormatException">The data is invalid.</exception>
    public static void ValidateSignal(string kind, string payload)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length > MaxSignalLength)
        {
            throw new FormatException("The signaling payload is too large.");
        }

        switch (kind)
        {
            case SignalKinds.Offer or SignalKinds.Answer when payload.StartsWith("v=0", StringComparison.Ordinal) && payload.Contains("\nm=", StringComparison.Ordinal):
                return;
            case SignalKinds.Offer or SignalKinds.Answer:
                throw new FormatException("The session description is not valid SDP.");
            case SignalKinds.Candidate:
                var separator = payload.IndexOf('\n');
                if (separator <= 0 || separator > 5 || !payload.AsSpan(0, separator).ToString().All(char.IsAsciiDigit)
                    || !payload.AsSpan(separator + 1).StartsWith("candidate:", StringComparison.Ordinal) || payload.Contains('\r'))
                {
                    throw new FormatException("The ICE candidate is malformed.");
                }

                return;
            default:
                throw new FormatException($"Unknown signaling kind '{kind}'.");
        }
    }

    /// <summary>
    /// Starts the pipeline; the offerer then negotiates automatically.
    /// </summary>
    public void Start()
    {
        // Negotiation waits until the offerer has opened the data channel, so the first offer carries it.
        _signaling.Wait();
        try
        {
            _pipeline.Play();
            if (IsOfferer)
            {
                lock (_nativeGate)
                {
                    var channel = Signals.EmitForObject(_pipeline.Element("webrtc"), "create-data-channel",
                        SignalArg.String(DataChannelLabel), SignalArg.Boxed(Gst.StructureGetType(), 0));
                    if (channel != 0)
                    {
                        AttachChannel(channel);
                    }
                    else
                    {
                        _logger.LogWarning("No data channel to {Peer}", Peer.ToShortString());
                    }
                }
            }
        }
        finally
        {
            _signaling.Release();
        }

        _ = Task.Run(VerifyRemoteCertificateAsync);
    }

    /// <summary>
    /// Extracts the SHA-256 fingerprint of the DTLS certificate from an SDP.
    /// </summary>
    /// <param name="sdp">The SDP.</param>
    /// <returns>The 32-byte fingerprint.</returns>
    /// <exception cref="FormatException">The SDP has no SHA-256 fingerprint or several different ones.</exception>
    public static byte[] ParseFingerprint(string sdp)
    {
        ArgumentNullException.ThrowIfNull(sdp);
        var values = FingerprintPattern().Matches(sdp).Select(m => m.Groups[1].Value.ToUpperInvariant()).Distinct().ToArray();
        if (values.Length != 1)
        {
            throw new FormatException("The SDP must contain exactly one SHA-256 DTLS fingerprint.");
        }

        return Convert.FromHexString(values[0].Replace(":", string.Empty, StringComparison.Ordinal));
    }

    /// <summary>
    /// Computes the SHA-256 fingerprint of a PEM certificate.
    /// </summary>
    /// <param name="pem">The certificate in PEM.</param>
    /// <returns>The fingerprint, or <see langword="null"/> when the text is not a certificate.</returns>
    public static byte[]? ComputeFingerprint(string? pem)
    {
        if (string.IsNullOrEmpty(pem) || !PemEncoding.TryFind(pem, out var fields) || pem[fields.Label] is not "CERTIFICATE")
        {
            return null;
        }

        var der = new byte[fields.DecodedDataLength];
        return Convert.TryFromBase64Chars(pem[fields.Base64Data], der, out var written) ? SHA256.HashData(der.AsSpan(0, written)) : null;
    }

    /// <summary>
    /// Sends an encoded video frame.
    /// </summary>
    /// <param name="data">The VP8 frame.</param>
    /// <param name="caps">The caps of the encoder output.</param>
    public void PushVideo(ReadOnlySpan<byte> data, nint caps) => PushEncoded("vsrc", data, caps, ref _videoCaps);

    /// <summary>
    /// Sends an encoded audio frame.
    /// </summary>
    /// <param name="data">The Opus frame.</param>
    /// <param name="caps">The caps of the encoder output.</param>
    public void PushAudio(ReadOnlySpan<byte> data, nint caps) => PushEncoded("asrc", data, caps, ref _audioCaps);

    /// <summary>
    /// Applies signaling data from the peer.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="payload">The payload.</param>
    /// <returns>A task that completes when the data was applied.</returns>
    /// <exception cref="FormatException">The payload is invalid.</exception>
    public async Task HandleSignalAsync(string kind, string payload)
    {
        ValidateSignal(kind, payload);
        await _signaling.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                if (_disposed)
                {
                    return;
                }

                switch (kind)
                {
                    case SignalKinds.Offer:
                        SetRemoteDescription(1, payload);
                        CreateAndSendDescription("create-answer", "answer", SignalKinds.Answer);
                        break;
                    case SignalKinds.Answer:
                        SetRemoteDescription(3, payload);
                        break;
                    case SignalKinds.Candidate:
                        var separator = payload.IndexOf('\n');
                        if (separator <= 0 || !uint.TryParse(payload.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var line))
                        {
                            throw new FormatException("The ICE candidate is malformed.");
                        }

                        Signals.Emit(_pipeline.Element("webrtc"), "add-ice-candidate", SignalArg.UInt(line), SignalArg.String(payload[(separator + 1)..]));
                        break;
                    default:
                        throw new FormatException($"Unknown signaling kind '{kind}'.");
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            _signaling.Release();
        }
    }

    /// <summary>
    /// Stops the pipeline and releases native resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_nativeGate)
        {
            _disposed = true;
            _verified = false;
            _pipeline.Dispose();
            _channelOpen = false;
            if (_channel != 0)
            {
                Gst.GstObjectUnref(_channel);
                _channel = 0;
            }
        }

        foreach (var receiver in _receivers)
        {
            Gst.GstObjectUnref(receiver);
        }

        _receivers.Clear();
        _self.Free();
    }

    /// <summary>
    /// Handles <c>on-negotiation-needed</c>.
    /// </summary>
    /// <param name="element">The webrtcbin.</param>
    /// <param name="data">The peer handle.</param>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnNegotiationNeeded(nint element, nint data)
    {
        if (From(data) is { IsOfferer: true, _disposed: false } peer)
        {
            _ = Task.Run(async () =>
            {
                await peer._signaling.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (!peer._disposed)
                    {
                        peer.CreateAndSendDescription("create-offer", "offer", SignalKinds.Offer);
                    }
                }
                catch (Exception ex) when (ex is GstException or FormatException)
                {
                    peer._logger.LogWarning(ex, "Offer to {Peer} failed", peer.Peer.ToShortString());
                }
                finally
                {
                    peer._signaling.Release();
                }
            });
        }
    }

    /// <summary>
    /// Handles <c>on-ice-candidate</c>.
    /// </summary>
    /// <param name="element">The webrtcbin.</param>
    /// <param name="line">The m-line index.</param>
    /// <param name="candidate">The candidate text.</param>
    /// <param name="data">The peer handle.</param>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnIceCandidate(nint element, uint line, nint candidate, nint data)
    {
        if (From(data) is { _disposed: false } peer)
        {
            var text = Marshal.PtrToStringUTF8(candidate) ?? string.Empty;
            if (text.Length == 0)
            {
                // The end of gathering carries no candidate; the other side does not need it.
                return;
            }

            peer._host.SendSignal(peer.Peer, SignalKinds.Candidate, line.ToString(CultureInfo.InvariantCulture) + "\n" + text);
        }
    }

    /// <summary>
    /// Handles <c>pad-added</c>: attaches a decoder for the incoming stream.
    /// </summary>
    /// <param name="element">The webrtcbin.</param>
    /// <param name="pad">The new source pad.</param>
    /// <param name="data">The peer handle.</param>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnPadAdded(nint element, nint pad, nint data)
    {
        if (From(data) is not { _disposed: false } peer)
        {
            return;
        }

        try
        {
            peer.AttachReceiver(pad);
        }
        catch (Exception ex) when (ex is GstException)
        {
            peer._logger.LogWarning(ex, "Incoming stream of {Peer} could not be decoded", peer.Peer.ToShortString());
        }
    }

    /// <summary>
    /// Handles <c>new-sample</c> of the video receiver.
    /// </summary>
    /// <param name="sink">The appsink.</param>
    /// <param name="data">The peer handle.</param>
    /// <returns><see cref="Samples.FlowOk"/>.</returns>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnVideoSample(nint sink, nint data)
    {
        if (From(data) is { _disposed: false } peer)
        {
            Samples.Pull(sink, (pixels, caps) =>
            {
                var (width, height) = Samples.VideoSize(caps);
                if (peer._verified && width > 0 && height > 0 && pixels.Length >= width * height * 4)
                {
                    Interlocked.Increment(ref peer._videoReceived);
                    peer._host.DeliverVideo(peer.Peer, width, height, pixels);
                }
            });
        }

        return Samples.FlowOk;
    }

    /// <summary>
    /// Handles <c>new-sample</c> of the audio receiver.
    /// </summary>
    /// <param name="sink">The appsink.</param>
    /// <param name="data">The peer handle.</param>
    /// <returns><see cref="Samples.FlowOk"/>.</returns>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnAudioSample(nint sink, nint data)
    {
        if (From(data) is { _disposed: false } peer)
        {
            Samples.Pull(sink, (pcm, _) =>
            {
                if (peer._verified)
                {
                    Interlocked.Increment(ref peer._audioReceived);
                    peer._host.DeliverAudio(peer, pcm);
                }
            });
        }

        return Samples.FlowOk;
    }

    /// <summary>
    /// Matches a SHA-256 fingerprint attribute of an SDP.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"^a=fingerprint:sha-256 ((?:[0-9A-Fa-f]{2}:){31}[0-9A-Fa-f]{2})\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex FingerprintPattern();

    /// <summary>
    /// Waits for the DTLS connection and compares the remote certificate with the signaled fingerprint.
    /// </summary>
    /// <returns>A task that completes when the certificate was checked or the peer was disposed.</returns>
    private async Task VerifyRemoteCertificateAsync()
    {
        while (!_disposed)
        {
            await Task.Delay(VerificationInterval).ConfigureAwait(false);
            var (state, pem) = ReadDtlsState();
            if (state == DtlsFailed)
            {
                _logger.LogWarning("DTLS with {Peer} failed", Peer.ToShortString());
                _host.RejectPeer(this, "DTLS failed");
                return;
            }

            if (state != DtlsConnected || _expectedFingerprint is not { } expected)
            {
                continue;
            }

            var actual = ComputeFingerprint(pem);
            if (actual is not null && CryptographicOperations.FixedTimeEquals(actual, expected))
            {
                OpenGates();
                _logger.LogInformation("DTLS certificate of {Peer} verified; media enabled", Peer.ToShortString());
            }
            else
            {
                _logger.LogWarning("DTLS certificate of {Peer} does not match the signaled fingerprint; disconnecting", Peer.ToShortString());
                _host.RejectPeer(this, "certificate mismatch");
            }

            return;
        }
    }

    /// <summary>
    /// Lets media through after the certificate has been verified. Until then the valves pass only caps and other
    /// sticky events, which webrtcbin needs to negotiate, and drop every buffer; incoming samples are discarded.
    /// </summary>
    private void OpenGates()
    {
        lock (_nativeGate)
        {
            if (_disposed)
            {
                return;
            }

            _verified = true;
            Gst.UtilSetObjectArg(_pipeline.Element("vgate"), "drop", "false");
            Gst.UtilSetObjectArg(_pipeline.Element("agate"), "drop", "false");
        }
    }

    /// <summary>
    /// Reads the state and the remote certificate of the DTLS transport of the first transceiver (all media is bundled).
    /// </summary>
    /// <returns>The state (-1 when not available yet) and the PEM certificate.</returns>
    private (int State, string? Pem) ReadDtlsState()
    {
        lock (_nativeGate)
        {
            if (_disposed)
            {
                return (-1, null);
            }

            var transceiver = Signals.EmitForObject(_pipeline.Element("webrtc"), "get-transceiver", SignalArg.Int(0));
            if (transceiver == 0)
            {
                return (-1, null);
            }

            nint receiver = 0, transport = 0;
            try
            {
                receiver = Signals.GetObject(transceiver, "receiver");
                transport = receiver == 0 ? 0 : Signals.GetObject(receiver, "transport");
                if (transport == 0)
                {
                    return (-1, null);
                }

                var state = Signals.GetEnum(transport, "state");
                return (state, state == DtlsConnected ? Signals.GetString(transport, "remote-certificate") : null);
            }
            finally
            {
                foreach (var instance in new[] { transport, receiver, transceiver })
                {
                    if (instance != 0)
                    {
                        Gst.GstObjectUnref(instance);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Handles <c>on-data-channel</c> of the answering side.
    /// </summary>
    /// <param name="element">The webrtcbin.</param>
    /// <param name="channel">The channel (not owned).</param>
    /// <param name="data">The peer handle.</param>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDataChannel(nint element, nint channel, nint data)
    {
        if (From(data) is { _disposed: false } peer && Signals.GetString(channel, "label") == DataChannelLabel)
        {
            lock (peer._nativeGate)
            {
                if (!peer._disposed && peer._channel == 0)
                {
                    peer.AttachChannel(Gst.ObjectRef(channel));
                }
            }
        }
    }

    /// <summary>
    /// Handles <c>on-open</c> of the data channel.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="data">The peer handle.</param>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnChannelOpen(nint channel, nint data)
    {
        if (From(data) is { _disposed: false } peer)
        {
            peer._channelOpen = true;
            peer._logger.LogInformation("Data channel to {Peer} is open", peer.Peer.ToShortString());
        }
    }

    /// <summary>
    /// Handles <c>on-close</c> of the data channel.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="data">The peer handle.</param>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnChannelClose(nint channel, nint data)
    {
        if (From(data) is { } peer)
        {
            peer._channelOpen = false;
        }
    }

    /// <summary>
    /// Handles <c>on-message-data</c>: passes messages of a verified peer on.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="bytes">The message.</param>
    /// <param name="data">The peer handle.</param>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnChannelData(nint channel, nint bytes, nint data)
    {
        if (bytes == 0 || From(data) is not { _disposed: false, _verified: true } peer)
        {
            return;
        }

        nuint size;
        var pointer = Gst.BytesGetData(bytes, &size);
        if (pointer != null && size <= int.MaxValue)
        {
            try
            {
                peer._host.DeliverData(peer.Peer, new ReadOnlySpan<byte>(pointer, (int)size));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                peer._logger.LogWarning(ex, "A data message of {Peer} failed", peer.Peer.ToShortString());
            }
        }
    }

    /// <summary>
    /// Keeps a data channel and listens to it. Call under <see cref="_nativeGate"/>.
    /// </summary>
    /// <param name="channel">The channel; the reference is taken over.</param>
    private unsafe void AttachChannel(nint channel)
    {
        _channel = channel;
        var data = GCHandle.ToIntPtr(_self);
        Gst.SignalConnectData(channel, "on-open", (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnChannelOpen, data, 0, 0);
        Gst.SignalConnectData(channel, "on-close", (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnChannelClose, data, 0, 0);
        Gst.SignalConnectData(channel, "on-message-data", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnChannelData, data, 0, 0);
        if (Signals.GetEnum(channel, "ready-state") == DataChannelOpen)
        {
            _channelOpen = true;
        }
    }

    /// <summary>
    /// Resolves a callback handle.
    /// </summary>
    /// <param name="data">The handle.</param>
    /// <returns>The peer or <see langword="null"/>.</returns>
    private static WebRtcPeer? From(nint data)
    {
        try
        {
            return GCHandle.FromIntPtr(data).Target as WebRtcPeer;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Pushes an encoded frame, setting the appsrc caps on the first frame.
    /// </summary>
    /// <param name="source">The appsrc name.</param>
    /// <param name="data">The frame.</param>
    /// <param name="caps">The encoder caps.</param>
    /// <param name="applied">The caps already applied.</param>
    private void PushEncoded(string source, ReadOnlySpan<byte> data, nint caps, ref nint applied)
    {
        if (_disposed)
        {
            return;
        }

        var element = _pipeline.Element(source);
        if (caps != 0 && caps != applied)
        {
            Gst.AppSrcSetCaps(element, caps);
            applied = caps;
        }

        Samples.Push(element, data);

        // Counted for the diagnostic report: a frame pushed here still goes nowhere while the valve is shut.
        if (source == "vsrc")
        {
            Interlocked.Increment(ref _videoSent);
        }
        else
        {
            Interlocked.Increment(ref _audioSent);
        }
    }

    /// <summary>
    /// Creates an offer or answer, applies it locally and sends it.
    /// </summary>
    /// <param name="action">The action signal.</param>
    /// <param name="field">The reply field.</param>
    /// <param name="kind">The signaling kind.</param>
    /// <exception cref="GstException">The negotiation failed.</exception>
    private unsafe void CreateAndSendDescription(string action, string field, string kind)
    {
        var webrtc = _pipeline.Element("webrtc");
        var promise = Gst.PromiseNew();
        try
        {
            Signals.Emit(webrtc, action, SignalArg.Boxed(Gst.StructureGetType(), 0), SignalArg.Boxed(Gst.PromiseGetType(), promise));
            if (Gst.PromiseWait(promise) != 2)
            {
                throw new GstException($"{action} was not answered.");
            }

            var reply = Gst.PromiseGetReply(promise);
            var value = reply == 0 ? null : Gst.StructureGetValue(reply, field);
            var description = value == null ? 0 : Gst.ValueGetBoxed(value);
            if (description == 0)
            {
                throw new GstException($"{action} returned no description.");
            }

            var sdp = Gst.TakeString(Gst.SdpMessageAsText(Marshal.ReadIntPtr(description, sizeof(nint))));
            Signals.Emit(webrtc, "set-local-description",
                SignalArg.Boxed(Gst.SessionDescriptionGetType(), description), SignalArg.Boxed(Gst.PromiseGetType(), 0));
            _host.SendSignal(Peer, kind, sdp);
        }
        finally
        {
            Gst.MiniObjectUnref(promise);
        }
    }

    /// <summary>
    /// Applies a remote description.
    /// </summary>
    /// <param name="type">1 offer, 3 answer.</param>
    /// <param name="text">The SDP text.</param>
    /// <exception cref="FormatException">The SDP is invalid.</exception>
    private unsafe void SetRemoteDescription(int type, string text)
    {
        nint message = 0;
        if (Gst.SdpMessageNewFromText(text, &message) != 0 || message == 0)
        {
            throw new FormatException("The SDP is invalid.");
        }

        var fingerprint = ParseFingerprint(text);
        if (_expectedFingerprint is { } known && !known.AsSpan().SequenceEqual(fingerprint))
        {
            Gst.SdpMessageFree(message);
            throw new FormatException("The DTLS fingerprint of the peer changed during the call.");
        }

        _expectedFingerprint = fingerprint;
        var description = Gst.SessionDescriptionNew(type, message);
        var promise = Gst.PromiseNew();
        try
        {
            Signals.Emit(_pipeline.Element("webrtc"), "set-remote-description",
                SignalArg.Boxed(Gst.SessionDescriptionGetType(), description), SignalArg.Boxed(Gst.PromiseGetType(), promise));
            Gst.PromiseWait(promise);
        }
        finally
        {
            Gst.MiniObjectUnref(promise);
            Gst.SessionDescriptionFree(description);
        }
    }

    /// <summary>
    /// Adds a decoding bin for an incoming RTP stream.
    /// </summary>
    /// <param name="pad">The webrtcbin source pad.</param>
    /// <exception cref="GstException">The bin could not be created or linked.</exception>
    private unsafe void AttachReceiver(nint pad)
    {
        var caps = Gst.PadGetCurrentCaps(pad);
        if (caps == 0)
        {
            caps = Gst.PadQueryCaps(pad, 0);
        }

        var media = Samples.CapsString(caps, "media");
        var capsText = caps == 0 ? "(none)" : Gst.TakeString(Gst.CapsToString(caps));
        if (caps != 0)
        {
            Gst.MiniObjectUnref(caps);
        }

        _logger.LogDebug("Pad added for {Peer}: {Caps}", Peer.ToShortString(), capsText);
        if (media.Length == 0)
        {
            media = capsText.Contains("encoding-name=(string)VP8", StringComparison.OrdinalIgnoreCase) ? "video"
                : capsText.Contains("encoding-name=(string)OPUS", StringComparison.OrdinalIgnoreCase) ? "audio"
                : string.Empty;
        }

        var (description, sinkName, callback) = media switch
        {
            "video" => (VideoReceiver, "rvideo", (nint)(delegate* unmanaged[Cdecl]<nint, nint, int>)&OnVideoSample),
            "audio" => (AudioReceiver, "raudio", (nint)(delegate* unmanaged[Cdecl]<nint, nint, int>)&OnAudioSample),
            _ => (string.Empty, string.Empty, (nint)0),
        };
        if (description.Length == 0)
        {
            _logger.LogWarning("Ignoring an incoming stream of {Peer} with caps {Caps}", Peer.ToShortString(), capsText);
            return;
        }

        GError* error = null;
        var bin = Gst.ParseBinFromDescription(description, 1, &error);
        if (error != null || bin == 0)
        {
            throw new GstException(Gst.TakeError(error));
        }

        if (Gst.BinAdd(_pipeline.Handle, bin) == 0)
        {
            throw new GstException("The receiver could not be added.");
        }

        var sink = Gst.BinGetByName(bin, sinkName);
        Gst.SignalConnectData(sink, "new-sample", callback, GCHandle.ToIntPtr(_self), 0, 0);
        lock (_receivers)
        {
            _receivers.Add(sink);
        }

        Gst.ElementSyncStateWithParent(bin);
        var sinkPad = Gst.ElementGetStaticPad(bin, "sink");
        try
        {
            if (Gst.PadLink(pad, sinkPad) != 0)
            {
                throw new GstException($"The {media} stream could not be linked.");
            }
        }
        finally
        {
            Gst.GstObjectUnref(sinkPad);
        }

        _logger.LogInformation("Receiving {Media} from {Peer}", media, Peer.ToShortString());
    }
}

/// <summary>
/// The servers a peer uses to find a path.
/// </summary>
/// <param name="StunServer">A STUN server or <see langword="null"/>.</param>
/// <param name="TurnServer">A TURN relay or <see langword="null"/>.</param>
/// <param name="RelayOnly">Whether only relayed paths are allowed.</param>
internal sealed record PeerNetwork(string? StunServer, string? TurnServer, bool RelayOnly);

/// <summary>
/// The owner of peers: sends signaling and receives decoded media.
/// </summary>
internal interface IPeerHost
{
    /// <summary>
    /// Sends signaling data to a peer.
    /// </summary>
    /// <param name="peer">The peer.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="payload">The payload.</param>
    void SendSignal(PeerId peer, string kind, string payload);

    /// <summary>
    /// Delivers a decoded BGRA frame.
    /// </summary>
    /// <param name="peer">The peer.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="pixels">The pixels, valid only during the call.</param>
    void DeliverVideo(PeerId peer, int width, int height, ReadOnlySpan<byte> pixels);

    /// <summary>
    /// Delivers decoded PCM (S16LE, 48 kHz, mono).
    /// </summary>
    /// <param name="peer">The peer.</param>
    /// <param name="pcm">The samples, valid only during the call.</param>
    void DeliverAudio(WebRtcPeer peer, ReadOnlySpan<byte> pcm);

    /// <summary>
    /// Drops a peer whose DTLS connection failed or could not be verified.
    /// </summary>
    /// <param name="peer">The peer.</param>
    /// <param name="reason">The reason for the log.</param>
    void RejectPeer(WebRtcPeer peer, string reason);

    /// <summary>
    /// Delivers a data channel message of a verified peer.
    /// </summary>
    /// <param name="peer">The peer.</param>
    /// <param name="message">The message, valid only during the call.</param>
    void DeliverData(PeerId peer, ReadOnlySpan<byte> message);
}
