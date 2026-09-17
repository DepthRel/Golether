using Golether.Core.Identity;
using Golether.Media.Conference;
using Microsoft.Extensions.Logging;

namespace Golether.Session;

/// <summary>
/// Connects the cameras and voices of a session: starts capture, keeps one connection per other participant and
/// passes signaling through the control channel.
/// </summary>
/// <remarks>
/// Of two participants, the one with the smaller identifier creates the offer, so both sides agree without extra
/// messages.
/// </remarks>
public sealed class ConferenceLink : IAsyncDisposable
{
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
    /// Serializes peer set updates.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// The connected participants.
    /// </summary>
    private readonly HashSet<PeerId> _connected = [];

    /// <summary>
    /// Whether the link was started.
    /// </summary>
    private bool _started;

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
        _ = RunAsync(() => _media.StartCaptureAsync(null, null, CancellationToken.None), "starting capture");
    }

    /// <summary>
    /// Connects to new participants and disconnects from those who left.
    /// </summary>
    /// <param name="participants">The current participants (this device is ignored).</param>
    /// <returns>A task that completes when the set is updated.</returns>
    public async Task SyncAsync(IEnumerable<PeerId> participants)
    {
        if (!_started)
        {
            return;
        }

        var wanted = participants.Where(p => p != _self && !p.IsEmpty).ToHashSet();
        await _gate.WaitAsync().ConfigureAwait(false);
        try
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
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Applies a signal received from a participant.
    /// </summary>
    /// <param name="source">The authenticated source.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="payload">The payload.</param>
    /// <returns>A task that completes when the signal was applied.</returns>
    public Task ReceiveAsync(PeerId source, string kind, string payload)
    {
        if (!_started || source == _self)
        {
            return Task.CompletedTask;
        }

        return RunAsync(() => _media.HandleSignalAsync(new ConferenceSignal(source, kind, payload), CancellationToken.None), "applying a signal");
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
        await RunAsync(_media.StopAsync, "stopping").ConfigureAwait(false);
        SetRelay(null);
        _connected.Clear();
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
