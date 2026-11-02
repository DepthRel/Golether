using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Golether.Core.Identity;
using Golether.Media.Conference;

namespace Golether.Session;

/// <summary>
/// Connects the cameras and voices of a session: starts capture, keeps one connection per other participant and
/// passes signaling through the control channel.
/// </summary>
/// <remarks>
/// Of two participants, the one with the smaller identifier creates the offer, so both sides agree without extra
/// messages. The work with the backend runs in a queue of its own, in the order it came: the loop that reads the
/// control channel only puts it there. A backend that stops answering (a native pipeline that hangs) then costs the
/// cameras, never the chat, the pen, the statuses or the goodbye that travel the same channel.
/// </remarks>
public sealed class ConferenceLink : IAsyncDisposable
{
    /// <summary>
    /// The most work waiting for the backend at once: a backend that stopped answering must not let the network fill
    /// the memory with signals.
    /// </summary>
    private const int MAX_PENDING = 256;

    /// <summary>
    /// How long stopping waits for the work in progress: a hung backend does not hold the end of the session.
    /// </summary>
    private static readonly TimeSpan _stopPatience = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The conferencing backend.
    /// </summary>
    private readonly IConferenceMedia _media;

    /// <summary>
    /// This device.
    /// </summary>
    private readonly PeerId _self;

    /// <summary>
    /// Sends signaling to a participant.
    /// </summary>
    private readonly Func<ConferenceSignal, Task> _send;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// The work for the backend, in the order it came.
    /// </summary>
    private readonly Channel<(Func<Task> Operation, string What)> _work = Channel.CreateBounded<(Func<Task> Operation, string What)>(
        new BoundedChannelOptions(MAX_PENDING) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });

    /// <summary>
    /// Takes the work out of the queue one by one.
    /// </summary>
    private Task _worker = Task.CompletedTask;

    /// <summary>
    /// The connected participants; only the queue touches them.
    /// </summary>
    private readonly HashSet<PeerId> _connected = [];

    /// <summary>
    /// Whether the link was started.
    /// </summary>
    private volatile bool _started;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConferenceLink"/> class.
    /// </summary>
    /// <param name="media">The conferencing backend.</param>
    /// <param name="self">This device.</param>
    /// <param name="send">Sends a signal to the participant named in it.</param>
    /// <param name="logger">The logger.</param>
    public ConferenceLink(IConferenceMedia media, PeerId self, Func<ConferenceSignal, Task> send, ILogger logger)
    {
        _media = media ?? throw new ArgumentNullException(nameof(media));
        _self = self;
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets a value indicating whether cameras and voices work.
    /// </summary>
    public bool IsActive => _started;

    /// <summary>
    /// Gets a value indicating whether the microphone of this device is off (also when cameras and voices do not work).
    /// </summary>
    public bool MicrophoneOff => !_started || _media.MicrophoneMuted;

    /// <summary>
    /// Gets a value indicating whether the camera of this device is off (also when cameras and voices do not work).
    /// </summary>
    public bool CameraOff => !_started || _media.CameraOff;

    /// <summary>
    /// Switches devices off at the request of the host; devices that are already off stay off and nothing is switched on.
    /// </summary>
    /// <param name="microphone">Whether to switch the microphone off.</param>
    /// <param name="camera">Whether to switch the camera off.</param>
    /// <returns><see langword="true"/> when a device was switched off.</returns>
    public bool SwitchOff(bool microphone, bool camera)
    {
        var microphoneOff = _media.MicrophoneMuted || microphone;
        var cameraOff = _media.CameraOff || camera;
        if (microphoneOff == _media.MicrophoneMuted && cameraOff == _media.CameraOff)
        {
            return false;
        }

        _media.SetMuted(microphoneOff, cameraOff);
        return true;
    }

    /// <summary>
    /// Determines which side offers.
    /// </summary>
    /// <param name="self">This device.</param>
    /// <param name="other">The other participant.</param>
    /// <returns><see langword="true"/> when this device creates the offer.</returns>
    public static bool IsOfferer(PeerId self, PeerId other) => string.CompareOrdinal(self.Value, other.Value) < 0;

    /// <summary>
    /// Sets the STUN server for the connections of this session.
    /// </summary>
    /// <param name="stunServer">The server address, or <see langword="null"/>.</param>
    public void SetStunServer(string? stunServer)
    {
        try
        {
            _media.SetStunServer(stunServer);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("STUN server ignored: {Reason}", ex.Message);
        }
    }

    /// <summary>
    /// Sets the TURN relay for the connections of this session.
    /// </summary>
    /// <param name="turnServer">The relay address, or <see langword="null"/>.</param>
    public void SetRelay(string? turnServer)
    {
        try
        {
            _media.SetRelay(turnServer);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Relay ignored: {Reason}", ex.Message);
        }
    }

    /// <summary>
    /// Starts capture when the backend is available.
    /// </summary>
    public void Start()
    {
        if (_started || !_media.IsAvailable)
        {
            return;
        }

        _started = true;
        _media.SignalReady += OnSignalReady;
        _worker = Task.Run(WorkAsync);
        Enqueue(() => _media.StartCaptureAsync(null, null, CancellationToken.None), "starting capture");
    }

    /// <summary>
    /// Connects to new participants and disconnects from those who left, in the queue of the backend.
    /// </summary>
    /// <param name="participants">The current participants (this device is ignored).</param>
    public void Sync(IEnumerable<PeerId> participants)
    {
        if (!_started)
        {
            return;
        }

        var wanted = participants.Where(p => p != _self && !p.IsEmpty).ToHashSet();
        Enqueue(() => UpdatePeersAsync(wanted), "updating the participants");
    }

    /// <summary>
    /// Applies a signal received from a participant, in the queue of the backend.
    /// </summary>
    /// <param name="source">The authenticated source.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="payload">The payload.</param>
    public void Receive(PeerId source, string kind, string payload)
    {
        if (!_started || source == _self)
        {
            return;
        }

        Enqueue(() => _media.HandleSignalAsync(new ConferenceSignal(source, kind, payload), CancellationToken.None), "applying a signal");
    }

    /// <summary>
    /// Stops the cameras and voices.
    /// </summary>
    /// <returns>A task that completes when everything is stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _media.SignalReady -= OnSignalReady;
        _work.Writer.TryComplete();
        await Task.WhenAny(_worker, Task.Delay(_stopPatience)).ConfigureAwait(false);
        await RunAsync(_media.StopAsync, "stopping").ConfigureAwait(false);
        SetRelay(null);
        SetStunServer(null);
        _connected.Clear();
    }

    /// <summary>
    /// Puts work into the queue of the backend; when the queue is full the backend has stopped answering, and the work
    /// is dropped.
    /// </summary>
    /// <param name="operation">The work.</param>
    /// <param name="what">The description for the log.</param>
    private void Enqueue(Func<Task> operation, string what)
    {
        if (!_work.Writer.TryWrite((operation, what)) && _started)
        {
            _logger.LogWarning("Conference: {What} dropped, the cameras do not answer", what);
        }
    }

    /// <summary>
    /// Takes the work out of the queue one by one, until the link stops.
    /// </summary>
    /// <returns>A task that completes when the queue is closed.</returns>
    private async Task WorkAsync()
    {
        await foreach (var (operation, what) in _work.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (!_started)
            {
                return;
            }

            await RunAsync(operation, what).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Connects to the new participants and disconnects from those who left. Runs in the queue of the backend.
    /// </summary>
    /// <param name="wanted">The other participants of the session.</param>
    /// <returns>A task that completes when the set is updated.</returns>
    private async Task UpdatePeersAsync(HashSet<PeerId> wanted)
    {
        foreach (var peer in wanted.Except(_connected).ToArray())
        {
            _connected.Add(peer);
            await RunAsync(() => _media.ConnectAsync(peer, IsOfferer(_self, peer), CancellationToken.None), "connecting").ConfigureAwait(false);
        }

        foreach (var peer in _connected.Except(wanted).ToArray())
        {
            _connected.Remove(peer);
            await RunAsync(() => _media.DisconnectAsync(peer), "disconnecting").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Forwards a signal of the backend.
    /// </summary>
    /// <param name="sender">The backend.</param>
    /// <param name="signal">The signal.</param>
    private void OnSignalReady(object? sender, ConferenceSignal signal)
        => _ = RunAsync(() => _send(signal), "sending a signal");

    /// <summary>
    /// Runs an operation and logs failures; cameras must never break the session.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <param name="what">The description for the log.</param>
    /// <returns>A task that always completes successfully.</returns>
    private async Task RunAsync(Func<Task> operation, string what)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning("Conference: {What} failed: {Reason}", what, ex.Message);
        }
    }
}
