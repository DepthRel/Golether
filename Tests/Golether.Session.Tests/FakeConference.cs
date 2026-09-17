using System.Collections.Concurrent;
using Golether.Core.Identity;
using Golether.Media.Conference;

namespace Golether.Session.Tests;

/// <summary>
/// A conference backend that records what the session asks it to do.
/// </summary>
internal sealed class FakeConference : IConferenceMedia
{
    /// <inheritdoc />
    public event EventHandler<ParticipantVideoFrame>? VideoFrameReceived
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public event EventHandler<ConferenceSignal>? SignalReady;

    /// <summary>
    /// Gets the connected peers and whether this side offers.
    /// </summary>
    public ConcurrentDictionary<PeerId, bool> Connected { get; } = new();

    /// <summary>
    /// Gets the received signals.
    /// </summary>
    public ConcurrentQueue<ConferenceSignal> Received { get; } = new();

    /// <summary>
    /// Gets a value indicating whether capture was started.
    /// </summary>
    public bool CaptureStarted { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the backend was stopped.
    /// </summary>
    public bool Stopped { get; private set; }

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public string? UnavailableReason => null;

    /// <summary>
    /// Emits a signal as if the backend produced it.
    /// </summary>
    /// <param name="signal">The signal.</param>
    public void Emit(ConferenceSignal signal) => SignalReady?.Invoke(this, signal);

    /// <inheritdoc />
    public IReadOnlyList<CaptureDevice> GetDevices() => [];

    /// <inheritdoc />
    public Task StartCaptureAsync(CaptureDevice? camera, CaptureDevice? microphone, CancellationToken cancellationToken)
    {
        CaptureStarted = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void SetMuted(bool microphoneMuted, bool cameraOff)
    {
        MicrophoneMuted = microphoneMuted;
        CameraOff = cameraOff;
        MuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public bool MicrophoneMuted { get; private set; }

    /// <inheritdoc />
    public bool CameraOff { get; private set; }

    /// <inheritdoc />
    public event EventHandler? MuteChanged;

    /// <inheritdoc />
    public Task ConnectAsync(PeerId peer, bool isOfferer, CancellationToken cancellationToken)
    {
        Connected[peer] = isOfferer;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task HandleSignalAsync(ConferenceSignal signal, CancellationToken cancellationToken)
    {
        Received.Enqueue(signal);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DisconnectAsync(PeerId peer)
    {
        Connected.TryRemove(peer, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void SetRelay(string? turnServer) => Relay = turnServer;

    /// <inheritdoc />
    public event PeerDataHandler? DataReceived;

    /// <summary>
    /// Gets or sets the participants that exchange data (all connected ones when <see langword="null"/>).
    /// </summary>
    public Func<IReadOnlyCollection<PeerId>>? DataPeersSource { get; set; }

    /// <summary>
    /// Gets or sets where sent data goes; <see langword="null"/> drops it.
    /// </summary>
    public Action<PeerId, byte[]>? DataSink { get; set; }

    /// <inheritdoc />
    public IReadOnlyCollection<PeerId> DataPeers => DataPeersSource?.Invoke() ?? [];

    /// <inheritdoc />
    public bool TrySendData(PeerId peer, ReadOnlySpan<byte> message)
    {
        if (DataSink is null || !DataPeers.Contains(peer))
        {
            return false;
        }

        DataSink(peer, message.ToArray());
        return true;
    }

    /// <summary>
    /// Delivers a data message as if it came from a participant.
    /// </summary>
    /// <param name="peer">The sender.</param>
    /// <param name="message">The message.</param>
    public void ReceiveData(PeerId peer, byte[] message) => DataReceived?.Invoke(this, peer, message);

    /// <inheritdoc />
    public event EventHandler<SpeakingChange>? SpeakingChanged
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Gets the relay set by the session.
    /// </summary>
    public string? Relay { get; private set; }

    /// <inheritdoc />
    public Task StopAsync()
    {
        Stopped = true;
        Connected.Clear();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
