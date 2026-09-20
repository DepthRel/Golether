using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Golether.Core.Identity;
using Golether.Media.Conference.GStreamer.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.Media.Conference.GStreamer;

/// <summary>
/// <see cref="IConferenceMedia"/> with GStreamer WebRTC.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Video capture: camera → 640×360@15 → preview (BGRA) and one VP8 encoder shared by all peers; a second,
/// economy encoder (320×180@10) runs only while a participant with a weak connection needs it.</item>
/// <item>Audio capture: microphone → echo cancellation and noise suppression (<c>webrtcdsp</c>) → one Opus encoder.</item>
/// <item>One pipeline with <c>webrtcbin</c> per remote participant (at most four).</item>
/// <item>Playback: the voices of all peers are mixed and pass <c>webrtcechoprobe</c>, the reference of the echo
/// canceller, before reaching the speakers.</item>
/// </list>
/// </remarks>
public sealed class GStreamerConferenceMedia : IConferenceMedia, IPeerHost
{
    /// <summary>
    /// The number of remote voices the mixer accepts.
    /// </summary>
    public const int MaxRemotePeers = 4;

    /// <summary>
    /// The options.
    /// </summary>
    private readonly GStreamerConferenceOptions _options;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// The peers.
    /// </summary>
    private readonly ConcurrentDictionary<PeerId, WebRtcPeer> _peers = new();

    /// <summary>
    /// The voice volumes of participants that differ from 1.
    /// </summary>
    private readonly ConcurrentDictionary<PeerId, double> _voiceVolumes = new();

    /// <summary>
    /// Guards pipeline creation and slot assignment.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The handle passed to native callbacks.
    /// </summary>
    private readonly GCHandle _self;

    /// <summary>
    /// The video capture pipeline.
    /// </summary>
    private GstPipeline? _videoCapture;

    /// <summary>
    /// The audio capture pipeline.
    /// </summary>
    private GstPipeline? _audioCapture;

    /// <summary>
    /// The playback pipeline.
    /// </summary>
    private GstPipeline? _playback;

    /// <summary>
    /// Whether the microphone is muted.
    /// </summary>
    private volatile bool _microphoneMuted;

    /// <summary>
    /// Whether the camera is off.
    /// </summary>
    private volatile bool _cameraOff;

    /// <summary>
    /// The camera chosen for capture, or <see langword="null"/> for the default one.
    /// </summary>
    private CaptureDevice? _camera;

    /// <summary>
    /// The microphone chosen for capture, or <see langword="null"/> for the default one.
    /// </summary>
    private CaptureDevice? _microphone;

    /// <summary>
    /// Whether capture has been started and not stopped.
    /// </summary>
    private bool _captureRequested;

    /// <summary>
    /// The TURN relay for new connections, or <see langword="null"/>.
    /// </summary>
    private volatile string? _turnServer;

    /// <summary>
    /// Tells who is speaking.
    /// </summary>
    private readonly VoiceActivityDetector _voices = new();

    /// <summary>
    /// Measures time for the voice detector.
    /// </summary>
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>
    /// Ends the speaking state of silent participants.
    /// </summary>
    private readonly Timer _voiceTimer;

    /// <summary>
    /// Chooses the camera stream of each participant.
    /// </summary>
    private readonly Timer? _qualityTimer;

    /// <summary>
    /// Whether a quality update runs.
    /// </summary>
    private int _qualityBusy;

    /// <summary>
    /// Initializes a new instance of the <see cref="GStreamerConferenceMedia"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="logger">The logger.</param>
    public GStreamerConferenceMedia(GStreamerConferenceOptions options, ILogger<GStreamerConferenceMedia>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<GStreamerConferenceMedia>.Instance;
        _self = GCHandle.Alloc(this);
        _voiceTimer = new Timer(_ => ExpireVoices(), null, TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(150));
        if (options.QualityInterval > TimeSpan.Zero)
        {
            _qualityTimer = new Timer(_ => UpdateVideoQuality(), null, options.QualityInterval, options.QualityInterval);
        }
        IsAvailable = GstRuntime.TryInitialize(options.BundleRoot, options.RegistryFile, out var error);
        UnavailableReason = error;
        if (IsAvailable)
        {
            _logger.LogInformation("{Version} is ready", GstRuntime.Version);
        }
    }

    /// <inheritdoc />
    public event EventHandler<ParticipantVideoFrame>? VideoFrameReceived;

    /// <inheritdoc />
    public event EventHandler<ConferenceSignal>? SignalReady;

    /// <inheritdoc />
    public event EventHandler? MuteChanged;

    /// <inheritdoc />
    public event EventHandler<VideoQualityChange>? VideoQualityChanged;

    /// <inheritdoc />
    public event EventHandler<SpeakingChange>? SpeakingChanged;

    /// <inheritdoc />
    public event PeerDataHandler? DataReceived;

    /// <inheritdoc />
    public IReadOnlyCollection<PeerId> DataPeers => _peers.Where(p => p.Value.IsDataReady).Select(p => p.Key).ToArray();

    /// <inheritdoc />
    public bool TrySendData(PeerId peer, ReadOnlySpan<byte> message)
        => _peers.TryGetValue(peer, out var connection) && connection.TrySendData(message);

    /// <inheritdoc />
    void IPeerHost.DeliverData(PeerId peer, ReadOnlySpan<byte> message) => DataReceived?.Invoke(this, peer, message);

    /// <inheritdoc />
    public void SetVoiceVolume(PeerId peer, double volume)
    {
        var clamped = PcmGain.Clamp(volume);
        if (clamped == 1)
        {
            _voiceVolumes.TryRemove(peer, out _);
        }
        else
        {
            _voiceVolumes[peer] = clamped;
        }
    }

    /// <inheritdoc />
    public void SetRelay(string? turnServer)
    {
        if (turnServer is not null && !turnServer.StartsWith("turn://", StringComparison.Ordinal) && !turnServer.StartsWith("turns://", StringComparison.Ordinal))
        {
            throw new ArgumentException("The relay must be a turn:// or turns:// address.", nameof(turnServer));
        }

        _turnServer = turnServer;
    }

    /// <inheritdoc />
    public bool MicrophoneMuted => _microphoneMuted;

    /// <inheritdoc />
    public bool CameraOff => _cameraOff;

    /// <inheritdoc />
    public bool IsAvailable { get; }

    /// <inheritdoc />
    public string? UnavailableReason { get; private set; }

    /// <summary>
    /// Gets the number of connected peers.
    /// </summary>
    public int PeerCount => _peers.Count;

    /// <inheritdoc />
    public IReadOnlyList<ConferencePeerDiagnostics> GetPeerDiagnostics()
        => [.. _peers.Values.Select(peer => peer.Describe())];

    /// <inheritdoc />
    public unsafe IReadOnlyList<CaptureDevice> GetDevices()
    {
        if (!IsAvailable)
        {
            return [];
        }

        var monitor = Gst.DeviceMonitorNew();
        try
        {
            Gst.DeviceMonitorAddFilter(monitor, "Video/Source", 0);
            Gst.DeviceMonitorAddFilter(monitor, "Audio/Source", 0);
            var list = Gst.DeviceMonitorGetDevices(monitor);
            var result = new List<CaptureDevice>();
            for (var node = list; node != null; node = (GList*)node->Next)
            {
                var device = node->Data;
                var name = Gst.TakeString(Gst.DeviceGetDisplayName(device));
                var properties = Gst.DeviceGetProperties(device);
                var api = properties == 0 ? string.Empty : ReadProperty(properties, "device.api");
                var kind = api switch
                {
                    "mediafoundation" or "v4l2" or "avf" or "libcamera" => CaptureDeviceKind.Camera,
                    _ => CaptureDeviceKind.Microphone,
                };
                var id = properties == 0 ? string.Empty
                    : kind == CaptureDeviceKind.Camera ? FirstProperty(properties, "device.path", "api.v4l2.path", "avf.unique_id")
                    : FirstProperty(properties, "device.id", "node.name", "device.strid");
                if (properties != 0)
                {
                    Gst.StructureFree(properties);
                }

                if (id.Length > 0)
                {
                    result.Add(new CaptureDevice(id, name, kind));
                }

                Gst.GstObjectUnref(device);
            }

            Gst.ListFree(list);
            return result;
        }
        finally
        {
            Gst.GstObjectUnref(monitor);
        }
    }

    /// <inheritdoc />
    public Task StartCaptureAsync(CaptureDevice? camera, CaptureDevice? microphone, CancellationToken cancellationToken)
    {
        EnsureAvailable();
        return Task.Run(() =>
        {
            lock (_gate)
            {
                _camera = camera;
                _microphone = microphone;
                _captureRequested = true;

                // The playback pipeline owns the echo probe the canceller of the microphone looks up on start.
                if (_options.EnablePlayback && _playback is null)
                {
                    _playback = TryStart("playback", BuildPlayback(), _ => { });
                }

                RestartVideo();
                RestartAudio();
                if (_videoCapture is null && _audioCapture is null && !_cameraOff && !_microphoneMuted)
                {
                    UnavailableReason = "Камера и микрофон недоступны.";
                }
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A camera that is off and a muted microphone are released, not only silenced: the device stops (and its
    /// indicator goes out) until it is switched on again.
    /// </remarks>
    public void SetMuted(bool microphoneMuted, bool cameraOff)
    {
        var cameraChanged = _cameraOff != cameraOff;
        var microphoneChanged = _microphoneMuted != microphoneMuted;
        _microphoneMuted = microphoneMuted;
        _cameraOff = cameraOff;
        if (cameraChanged || microphoneChanged)
        {
            MuteChanged?.Invoke(this, EventArgs.Empty);
        }

        if (!IsAvailable || !(cameraChanged || microphoneChanged))
        {
            return;
        }

        _ = Task.Run(() =>
        {
            lock (_gate)
            {
                if (!_captureRequested)
                {
                    return;
                }

                if (cameraChanged)
                {
                    RestartVideo();
                }

                if (microphoneChanged)
                {
                    RestartAudio();
                }
            }
        });
    }

    /// <summary>
    /// Recreates the camera pipeline for the current device, or only stops it while the camera is off. Call under
    /// <see cref="_gate"/>.
    /// </summary>
    private void RestartVideo()
    {
        _videoCapture?.Dispose();
        _videoCapture = _cameraOff ? null : TryStart("camera", BuildVideoCapture(_camera), pipeline =>
        {
            Connect(pipeline.Element("preview"), Callbacks.Preview);
            Connect(pipeline.Element("venc"), Callbacks.EncodedVideo);
            Connect(pipeline.Element("venclow"), Callbacks.EncodedVideoLow);
            SetEconomyEncoder(pipeline, _peers.Values.Any(p => p.VideoQuality == VideoQuality.Low));
        });
    }

    /// <summary>
    /// Recreates the microphone pipeline for the current device, or only stops it while the microphone is muted.
    /// Call under <see cref="_gate"/>.
    /// </summary>
    private void RestartAudio()
    {
        _audioCapture?.Dispose();
        _audioCapture = null;
        if (_microphoneMuted)
        {
            return;
        }

        if (_playback is not null)
        {
            _audioCapture = TryStart("microphone", BuildAudioCapture(_microphone, echoCancellation: true), pipeline =>
            {
                Connect(pipeline.Element("aenc"), Callbacks.EncodedAudio);
                Connect(pipeline.Element("alevel"), Callbacks.MicrophoneLevel);
            });
        }

        _audioCapture ??= TryStart("microphone (without echo cancellation)", BuildAudioCapture(_microphone, echoCancellation: false), pipeline =>
        {
            Connect(pipeline.Element("aenc"), Callbacks.EncodedAudio);
            Connect(pipeline.Element("alevel"), Callbacks.MicrophoneLevel);
        });
    }

    /// <inheritdoc />
    public Task ConnectAsync(PeerId peer, bool isOfferer, CancellationToken cancellationToken)
    {
        EnsureAvailable();
        return Task.Run(() => GetOrCreatePeer(peer, isOfferer), cancellationToken);
    }

    /// <inheritdoc />
    public async Task HandleSignalAsync(ConferenceSignal signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        EnsureAvailable();
        WebRtcPeer.ValidateSignal(signal.Kind, signal.Payload);

        // The side that receives an offer first answers it.
        var peer = _peers.TryGetValue(signal.PeerId, out var existing)
            ? existing
            : await Task.Run(() => GetOrCreatePeer(signal.PeerId, isOfferer: false), cancellationToken).ConfigureAwait(false);
        if (peer is not null)
        {
            await peer.HandleSignalAsync(signal.Kind, signal.Payload).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task DisconnectAsync(PeerId peer)
        => Task.Run(() =>
        {
            if (_peers.TryRemove(peer, out var removed))
            {
                removed.Dispose();
                _logger.LogInformation("Disconnected from {Peer}", peer.ToShortString());
            }
        });

    /// <inheritdoc />
    public async Task StopAsync()
    {
        foreach (var peer in _peers.Keys.ToArray())
        {
            await DisconnectAsync(peer).ConfigureAwait(false);
        }

        await Task.Run(() =>
        {
            lock (_gate)
            {
                _captureRequested = false;
                _videoCapture?.Dispose();
                _audioCapture?.Dispose();
                _playback?.Dispose();
                _videoCapture = _audioCapture = _playback = null;
            }
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await _voiceTimer.DisposeAsync().ConfigureAwait(false);
        if (_qualityTimer is not null)
        {
            await _qualityTimer.DisposeAsync().ConfigureAwait(false);
        }
        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    /// <inheritdoc />
    void IPeerHost.SendSignal(PeerId peer, string kind, string payload)
        => SignalReady?.Invoke(this, new ConferenceSignal(peer, kind, payload));

    /// <inheritdoc />
    void IPeerHost.DeliverVideo(PeerId peer, int width, int height, ReadOnlySpan<byte> pixels)
        => RaiseFrame(peer, width, height, pixels);

    /// <inheritdoc />
    void IPeerHost.DeliverAudio(WebRtcPeer peer, ReadOnlySpan<byte> pcm)
    {
        var playback = _playback;
        if (playback is not null)
        {
            if (_voiceVolumes.TryGetValue(peer.Peer, out var volume))
            {
                var scaled = System.Buffers.ArrayPool<byte>.Shared.Rent(pcm.Length);
                try
                {
                    pcm.CopyTo(scaled);
                    PcmGain.Apply(scaled.AsSpan(0, pcm.Length), volume);
                    Samples.Push(playback.Element($"slot{peer.AudioSlot}"), scaled.AsSpan(0, pcm.Length));
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(scaled);
                }
            }
            else
            {
                Samples.Push(playback.Element($"slot{peer.AudioSlot}"), pcm);
            }
        }

        DetectVoice(peer.Peer, pcm);
    }

    /// <summary>
    /// Feeds 48 kHz mono 16-bit audio of a participant into the voice detector.
    /// </summary>
    /// <param name="peer">The participant, or <see langword="default"/> for this device.</param>
    /// <param name="pcm">The samples.</param>
    private void DetectVoice(PeerId peer, ReadOnlySpan<byte> pcm)
    {
        var duration = TimeSpan.FromSeconds(pcm.Length / 2 / 48000.0);
        if (_voices.Process(peer, pcm, duration, _clock.Elapsed) is { } change)
        {
            RaiseSpeaking(change);
        }
    }

    /// <summary>
    /// Ends the speaking state of participants that went quiet.
    /// </summary>
    private void ExpireVoices()
    {
        foreach (var change in _voices.Expire(_clock.Elapsed))
        {
            RaiseSpeaking(change);
        }
    }

    /// <summary>
    /// Raises <see cref="SpeakingChanged"/>, ignoring subscriber failures.
    /// </summary>
    /// <param name="change">The change.</param>
    private void RaiseSpeaking(SpeakingChange change)
    {
        try
        {
            SpeakingChanged?.Invoke(this, change);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "A speaking subscriber failed");
        }
    }

    /// <inheritdoc />
    void IPeerHost.RejectPeer(WebRtcPeer peer, string reason)
    {
        _logger.LogWarning("Dropping the media connection of {Peer}: {Reason}", peer.Peer.ToShortString(), reason);
        _ = Task.Run(() =>
        {
            // Only this exact connection is removed; a newer one for the same participant stays.
            if (_peers.TryRemove(new KeyValuePair<PeerId, WebRtcPeer>(peer.Peer, peer)))
            {
                peer.Dispose();
            }
        });
    }

    /// <summary>
    /// Handles preview frames.
    /// </summary>
    /// <param name="sink">The appsink.</param>
    /// <param name="data">The media handle.</param>
    /// <returns><see cref="Samples.FlowOk"/>.</returns>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int OnPreviewSample(nint sink, nint data)
    {
        if (From(data) is { } media)
        {
            Samples.Pull(sink, (pixels, caps) =>
            {
                var (width, height) = Samples.VideoSize(caps);
                if (width > 0 && !media._cameraOff && pixels.Length >= width * height * 4)
                {
                    media.RaiseFrame(default, width, height, pixels);
                }
            });
        }

        return Samples.FlowOk;
    }

    /// <summary>
    /// Fans the encoded video out to all peers.
    /// </summary>
    /// <param name="sink">The appsink.</param>
    /// <param name="data">The media handle.</param>
    /// <returns><see cref="Samples.FlowOk"/>.</returns>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnEncodedVideo(nint sink, nint data)
    {
        if (From(data) is { } media)
        {
            Samples.Pull(sink, (frame, caps) =>
            {
                if (media._cameraOff)
                {
                    return;
                }

                foreach (var peer in media._peers.Values)
                {
                    peer.PushVideo(frame, caps, VideoQuality.High);
                }
            });
        }

        return Samples.FlowOk;
    }

    /// <summary>
    /// Fans the economy video stream out to the peers with weak connections.
    /// </summary>
    /// <param name="sink">The appsink.</param>
    /// <param name="data">The media handle.</param>
    /// <returns><see cref="Samples.FlowOk"/>.</returns>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnEncodedVideoLow(nint sink, nint data)
    {
        if (From(data) is { } media)
        {
            Samples.Pull(sink, (frame, caps) =>
            {
                if (media._cameraOff)
                {
                    return;
                }

                foreach (var peer in media._peers.Values)
                {
                    peer.PushVideo(frame, caps, VideoQuality.Low);
                }
            });
        }

        return Samples.FlowOk;
    }

    /// <summary>
    /// Chooses the camera stream of every participant from the connection statistics and runs the economy encoder
    /// only while somebody needs it.
    /// </summary>
    internal void UpdateVideoQuality()
    {
        if (Interlocked.Exchange(ref _qualityBusy, 1) != 0)
        {
            return;
        }

        try
        {
            var changes = new List<VideoQualityChange>();
            foreach (var peer in _peers.Values)
            {
                try
                {
                    if (peer.UpdateVideoQuality())
                    {
                        changes.Add(new VideoQualityChange(peer.Peer, peer.VideoQuality));
                    }
                }
                catch (Exception ex) when (ex is GstException or ObjectDisposedException or InvalidOperationException)
                {
                    _logger.LogDebug("Quality of {Peer} not updated: {Error}", peer.Peer.ToShortString(), ex.Message);
                }
            }

            if (changes.Count == 0)
            {
                return;
            }

            lock (_gate)
            {
                if (_videoCapture is { } capture)
                {
                    SetEconomyEncoder(capture, _peers.Values.Any(p => p.VideoQuality == VideoQuality.Low));
                }
            }

            foreach (var change in changes)
            {
                VideoQualityChanged?.Invoke(this, change);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _qualityBusy, 0);
        }
    }

    /// <summary>
    /// Reads the connection statistics of a participant (tests).
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <returns>The loss and round trip, when reported.</returns>
    internal (double? Loss, double? RoundTrip) ReadNetworkStats(PeerId peer)
        => _peers.TryGetValue(peer, out var connection) ? connection.ReadNetworkStats() : (null, null);

    /// <summary>
    /// Sends a participant the given camera stream regardless of the statistics (tests).
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="quality">The stream.</param>
    internal void ForceVideoQuality(PeerId peer, VideoQuality quality)
    {
        if (_peers.TryGetValue(peer, out var connection))
        {
            connection.SetVideoQuality(quality);
        }

        lock (_gate)
        {
            if (_videoCapture is { } capture)
            {
                SetEconomyEncoder(capture, _peers.Values.Any(p => p.VideoQuality == VideoQuality.Low));
            }
        }
    }

    /// <summary>
    /// Opens or closes the input of the economy encoder.
    /// </summary>
    /// <param name="capture">The camera pipeline.</param>
    /// <param name="needed">Whether a participant gets the economy stream.</param>
    private static void SetEconomyEncoder(GstPipeline capture, bool needed)
        => Gst.UtilSetObjectArg(capture.Element("lowgate"), "drop", needed ? "false" : "true");

    /// <summary>
    /// Fans the encoded audio out to all peers.
    /// </summary>
    /// <param name="sink">The appsink.</param>
    /// <param name="data">The media handle.</param>
    /// <returns><see cref="Samples.FlowOk"/>.</returns>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnEncodedAudio(nint sink, nint data)
    {
        if (From(data) is { } media)
        {
            Samples.Pull(sink, (frame, caps) =>
            {
                if (media._microphoneMuted)
                {
                    return;
                }

                foreach (var peer in media._peers.Values)
                {
                    peer.PushAudio(frame, caps);
                }
            });
        }

        return Samples.FlowOk;
    }

    /// <summary>
    /// Measures the loudness of this device's microphone.
    /// </summary>
    /// <param name="sink">The appsink.</param>
    /// <param name="data">The media handle.</param>
    /// <returns><see cref="Samples.FlowOk"/>.</returns>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnMicrophoneLevel(nint sink, nint data)
    {
        if (From(data) is { } media)
        {
            Samples.Pull(sink, (pcm, _) =>
            {
                if (!media._microphoneMuted)
                {
                    media.DetectVoice(default, pcm);
                }
            });
        }

        return Samples.FlowOk;
    }

    /// <summary>
    /// The addresses of the native callbacks.
    /// </summary>
    private static unsafe class Callbacks
    {
        /// <summary>
        /// Gets the preview callback.
        /// </summary>
        public static nint Preview => (nint)(delegate* unmanaged[Cdecl]<nint, nint, int>)&OnPreviewSample;

        /// <summary>
        /// Gets the encoded video callback.
        /// </summary>
        public static nint EncodedVideo => (nint)(delegate* unmanaged[Cdecl]<nint, nint, int>)&OnEncodedVideo;

        /// <summary>
        /// Gets the economy video callback.
        /// </summary>
        public static nint EncodedVideoLow => (nint)(delegate* unmanaged[Cdecl]<nint, nint, int>)&OnEncodedVideoLow;

        /// <summary>
        /// Gets the encoded audio callback.
        /// </summary>
        public static nint EncodedAudio => (nint)(delegate* unmanaged[Cdecl]<nint, nint, int>)&OnEncodedAudio;

        /// <summary>
        /// Gets the microphone level callback.
        /// </summary>
        public static nint MicrophoneLevel => (nint)(delegate* unmanaged[Cdecl]<nint, nint, int>)&OnMicrophoneLevel;
    }

    /// <summary>
    /// Resolves a callback handle.
    /// </summary>
    /// <param name="data">The handle.</param>
    /// <returns>The media or <see langword="null"/>.</returns>
    private static GStreamerConferenceMedia? From(nint data)
    {
        try
        {
            return GCHandle.FromIntPtr(data).Target as GStreamerConferenceMedia;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a string property of a device.
    /// </summary>
    /// <param name="properties">The properties structure.</param>
    /// <param name="name">The property.</param>
    /// <returns>The value or an empty string.</returns>
    private static string ReadProperty(nint properties, string name)
    {
        var value = Gst.StructureGetString(properties, name);
        return value == 0 ? string.Empty : Marshal.PtrToStringUTF8(value) ?? string.Empty;
    }

    /// <summary>
    /// Returns the first present property.
    /// </summary>
    /// <param name="properties">The properties structure.</param>
    /// <param name="names">The property names.</param>
    /// <returns>The value or an empty string.</returns>
    private static string FirstProperty(nint properties, params string[] names)
        => names.Select(n => ReadProperty(properties, n)).FirstOrDefault(v => v.Length > 0) ?? string.Empty;

    /// <summary>
    /// Quotes a value for a pipeline description.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The quoted value.</returns>
    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    /// <summary>
    /// Builds the video capture description.
    /// </summary>
    /// <param name="camera">The camera or <see langword="null"/> for the default.</param>
    /// <returns>The description.</returns>
    private string BuildVideoCapture(CaptureDevice? camera)
    {
        var source = _options.UseTestSources ? "videotestsrc is-live=true pattern=ball"
            : OperatingSystem.IsWindows() ? "mfvideosrc" + (camera is null ? string.Empty : " device-path=" + Quote(camera.Id))
            : OperatingSystem.IsMacOS() ? "avfvideosrc" + (camera is null ? string.Empty : " device-unique-id=" + Quote(camera.Id))
            : camera is null ? "autovideosrc" : "v4l2src device=" + Quote(camera.Id);
        return $"{source} ! videoconvert ! videoscale ! videorate ! " +
               "video/x-raw,width=640,height=360,framerate=15/1,pixel-aspect-ratio=1/1 ! tee name=split " +
               "split. ! queue leaky=downstream max-size-buffers=2 ! videoconvert ! video/x-raw,format=BGRA ! " +
               "appsink name=preview emit-signals=true max-buffers=1 drop=true sync=false " +
               "split. ! queue leaky=downstream max-size-buffers=5 ! videoconvert ! video/x-raw,format=I420 ! " +
               $"vp8enc deadline=1 cpu-used=8 target-bitrate={_options.VideoBitrate} keyframe-max-dist=30 lag-in-frames=0 " +
               "error-resilient=partitions threads=2 ! appsink name=venc emit-signals=true sync=false max-buffers=10 drop=true " +
               "split. ! queue leaky=downstream max-size-buffers=5 ! valve name=lowgate drop=true drop-mode=forward-sticky-events ! videoscale ! videorate ! " +
               "video/x-raw,width=320,height=180,framerate=10/1,pixel-aspect-ratio=1/1 ! videoconvert ! video/x-raw,format=I420 ! " +
               $"vp8enc deadline=1 cpu-used=16 target-bitrate={_options.LowVideoBitrate} keyframe-max-dist=10 lag-in-frames=0 " +
               "error-resilient=partitions threads=1 ! appsink name=venclow emit-signals=true sync=false async=false max-buffers=10 drop=true";
    }

    /// <summary>
    /// Builds the audio capture description.
    /// </summary>
    /// <param name="microphone">The microphone or <see langword="null"/> for the default.</param>
    /// <param name="echoCancellation">Whether to use <c>webrtcdsp</c>.</param>
    /// <returns>The description.</returns>
    private string BuildAudioCapture(CaptureDevice? microphone, bool echoCancellation)
    {
        var source = _options.UseTestSources ? "audiotestsrc is-live=true wave=sine freq=440 volume=0.2"
            : OperatingSystem.IsWindows() ? "wasapi2src low-latency=true" + (microphone is null ? string.Empty : " device=" + Quote(microphone.Id))
            : OperatingSystem.IsMacOS() ? "osxaudiosrc"
            : microphone is null ? "autoaudiosrc" : "pulsesrc device=" + Quote(microphone.Id);
        var dsp = echoCancellation ? "webrtcdsp probe=golether-echo echo-cancel=true noise-suppression=true gain-control=true ! " : string.Empty;
        return $"{source} ! audioconvert ! audioresample ! audio/x-raw,format=S16LE,rate=48000,channels=1 ! {dsp}" +
               "audioconvert ! audio/x-raw,format=S16LE,rate=48000,channels=1 ! tee name=mic " +
               "mic. ! queue max-size-buffers=50 leaky=downstream ! opusenc bitrate=32000 frame-size=20 ! " +
               "appsink name=aenc emit-signals=true sync=false max-buffers=50 drop=true " +
               "mic. ! queue max-size-buffers=10 leaky=downstream ! appsink name=alevel emit-signals=true sync=false max-buffers=10 drop=true";
    }

    /// <summary>
    /// Builds the playback description with one mixer input per remote peer.
    /// </summary>
    /// <returns>The description.</returns>
    private static string BuildPlayback()
    {
        var sink = OperatingSystem.IsWindows() ? "wasapi2sink low-latency=true" : "autoaudiosink";
        var slots = string.Concat(Enumerable.Range(0, MaxRemotePeers).Select(i =>
            $" appsrc name=slot{i} is-live=true do-timestamp=true format=time " +
            "caps=\"audio/x-raw,format=S16LE,rate=48000,channels=1,layout=interleaved\" ! queue ! mix."));
        return $"audiomixer name=mix latency=60000000 ! audioconvert ! audioresample ! webrtcechoprobe name=golether-echo ! " +
               $"audioconvert ! audioresample ! {sink}{slots}";
    }

    /// <summary>
    /// Creates and starts a pipeline, logging failures.
    /// </summary>
    /// <param name="name">The pipeline name.</param>
    /// <param name="description">The description.</param>
    /// <param name="connect">Connects callbacks before start.</param>
    /// <returns>The pipeline, or <see langword="null"/> when it could not start.</returns>
    private GstPipeline? TryStart(string name, string description, Action<GstPipeline> connect)
    {
        GstPipeline? pipeline = null;
        try
        {
            pipeline = new GstPipeline(name, description, _logger);
            connect(pipeline);
            pipeline.Play();
            _logger.LogInformation("GStreamer {Pipeline} started", name);
            return pipeline;
        }
        catch (GstException ex)
        {
            _logger.LogWarning("GStreamer {Pipeline} is not available: {Reason}", name, ex.Message);
            pipeline?.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Connects a <c>new-sample</c> handler.
    /// </summary>
    /// <param name="appSink">The appsink.</param>
    /// <param name="callback">The callback.</param>
    private void Connect(nint appSink, nint callback)
        => Gst.SignalConnectData(appSink, "new-sample", callback, GCHandle.ToIntPtr(_self), 0, 0);

    /// <summary>
    /// Returns the peer or creates and starts it.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="isOfferer">Whether this side offers.</param>
    /// <returns>The peer, or <see langword="null"/> when all slots are taken.</returns>
    private WebRtcPeer? GetOrCreatePeer(PeerId peer, bool isOfferer)
    {
        lock (_gate)
        {
            if (_peers.TryGetValue(peer, out var existing))
            {
                return existing;
            }

            var used = _peers.Values.Select(p => p.AudioSlot).ToHashSet();
            var slot = Enumerable.Range(0, MaxRemotePeers).FirstOrDefault(i => !used.Contains(i), -1);
            if (slot < 0)
            {
                _logger.LogWarning("No free slot for {Peer}", peer.ToShortString());
                return null;
            }

            var created = new WebRtcPeer(peer, isOfferer, slot, this, new PeerNetwork(_options.StunServer, _turnServer, _options.RelayOnly), _logger);
            _peers[peer] = created;
            created.Start();
            _logger.LogInformation("Connecting to {Peer} as {Role}", peer.ToShortString(), isOfferer ? "offerer" : "answerer");
            return created;
        }
    }

    /// <summary>
    /// Raises a frame event, ignoring subscriber failures.
    /// </summary>
    /// <param name="peer">The participant or <see langword="default"/> for the preview.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="pixels">The pixels.</param>
    private void RaiseFrame(PeerId peer, int width, int height, ReadOnlySpan<byte> pixels)
    {
        var handler = VideoFrameReceived;
        if (handler is null)
        {
            return;
        }

        // The event carries a copy: subscribers may keep the frame beyond the callback.
        var copy = pixels[..(width * height * 4)].ToArray();
        try
        {
            handler(this, new ParticipantVideoFrame(peer, new VideoFrame(width, height, width * 4, copy)));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "A video frame subscriber failed");
        }
    }

    /// <summary>
    /// Throws when GStreamer is not available.
    /// </summary>
    /// <exception cref="InvalidOperationException">GStreamer is not available.</exception>
    private void EnsureAvailable()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException(UnavailableReason ?? "GStreamer is not available.");
        }
    }
}
