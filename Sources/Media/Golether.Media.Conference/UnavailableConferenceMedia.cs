using Golether.Core.Identity;
using Golether.Localization;

namespace Golether.Media.Conference;

/// <summary>
/// The placeholder used while no conferencing backend is available.
/// </summary>
public sealed class UnavailableConferenceMedia : IConferenceMedia
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnavailableConferenceMedia"/> class.
    /// </summary>
    /// <param name="reason">The reason shown to the user.</param>
    public UnavailableConferenceMedia(string reason)
        : this(() => reason)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UnavailableConferenceMedia"/> class.
    /// </summary>
    /// <param name="reason">Words the reason shown to the user each time it is asked, so it follows the language of
    /// the moment.</param>
    public UnavailableConferenceMedia(Func<string> reason)
    {
        _reason = reason ?? throw new ArgumentNullException(nameof(reason));
    }

    /// <summary>
    /// Words the reason.
    /// </summary>
    private readonly Func<string> _reason;

    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public string? UnavailableReason => _reason() is { } reason && !string.IsNullOrWhiteSpace(reason) ? reason : Texts.Get("Conference.Unavailable");

    /// <inheritdoc />
    public event EventHandler<ParticipantVideoFrame>? VideoFrameReceived
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public event EventHandler<ConferenceSignal>? SignalReady
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public IReadOnlyList<CaptureDevice> GetDevices() => [];

    /// <inheritdoc />
    public Task StartCaptureAsync(CaptureDevice? camera, CaptureDevice? microphone, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public void SetMuted(bool microphoneMuted, bool cameraOff)
    {
        if (MicrophoneMuted == microphoneMuted && CameraOff == cameraOff)
        {
            return;
        }

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
    public Task ConnectAsync(PeerId peer, bool isOfferer, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task HandleSignalAsync(ConferenceSignal signal, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task DisconnectAsync(PeerId peer) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public void SetRelay(string? turnServer)
    {
    }

    /// <inheritdoc />
    public void SetVoiceVolume(PeerId peer, double volume)
    {
    }

    /// <inheritdoc />
    public event PeerDataHandler? DataReceived
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<PeerId> DataPeers => [];

    /// <inheritdoc />
    public bool TrySendData(PeerId peer, ReadOnlySpan<byte> message) => false;

    /// <inheritdoc />
    public event EventHandler<SpeakingChange>? SpeakingChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public event EventHandler<VideoQualityChange>? VideoQualityChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
