using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Core.Identity;
using Golether.Localization;
using Golether.Session;

namespace Golether.UI.ViewModels;

/// <summary>
/// A participant tile in the side panel.
/// </summary>
public sealed partial class ParticipantItemViewModel : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ParticipantItemViewModel"/> class.
    /// </summary>
    /// <param name="peerId">The participant.</param>
    /// <param name="switchOff">Switches devices of the participant off (microphone, camera); used by the host.</param>
    /// <param name="voiceVolumeChanged">Applies and stores the local volume of the participant's voice (percent).</param>
    public ParticipantItemViewModel(PeerId peerId, Func<PeerId, bool, bool, Task>? switchOff = null, Action<PeerId, double>? voiceVolumeChanged = null)
    {
        _switchOff = switchOff;
        _voiceVolumeChanged = voiceVolumeChanged;
        PeerId = peerId;
        AvatarColor = ParticipantColors.For(peerId);
    }

    /// <summary>
    /// Switches devices of the participant off, or <see langword="null"/>.
    /// </summary>
    private readonly Func<PeerId, bool, bool, Task>? _switchOff;

    /// <summary>
    /// Applies the voice volume, or <see langword="null"/>.
    /// </summary>
    private readonly Action<PeerId, double>? _voiceVolumeChanged;

    /// <summary>
    /// The audible volume restored by "unmute".
    /// </summary>
    private double _lastAudibleVolume = 100;

    /// <summary>
    /// Whether a stored volume is being applied (no notification back).
    /// </summary>
    private bool _restoringVolume;

    /// <summary>
    /// Gets the participant.
    /// </summary>
    public PeerId PeerId { get; }

    /// <summary>
    /// Gets or sets a value indicating whether this device hosts the session and may switch the devices of the
    /// participant off.
    /// </summary>
    [ObservableProperty]
    public partial bool CanModerate { get; set; }

    /// <summary>
    /// The loudest a voice is played: the original level. Above it the voice only starts to clip.
    /// </summary>
    public const double MaxVoiceVolume = 100;

    /// <summary>
    /// Gets or sets how loud the participant's voice is played on this device, 0–100 %. Only this device hears the
    /// change.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VoiceVolumeText), nameof(IsVoiceMuted), nameof(VoiceMuteText))]
    public partial double VoiceVolume { get; set; } = MaxVoiceVolume;

    /// <summary>
    /// Gets the voice volume text.
    /// </summary>
    public string VoiceVolumeText => IsVoiceMuted
        ? Texts.Get("Participant.VoiceMuted")
        : Texts.Format("Format.Percent", Math.Round(VoiceVolume).ToString("0", System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Gets a value indicating whether the voice is silent on this device.
    /// </summary>
    public bool IsVoiceMuted => VoiceVolume <= 0;

    /// <summary>
    /// Gets the text of the mute command.
    /// </summary>
    public string VoiceMuteText => Texts.Get(IsVoiceMuted ? "Participant.VoiceUnmute" : "Participant.VoiceMute");

    /// <summary>
    /// Tells the view that the texts of the tile changed with the language.
    /// </summary>
    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(VoiceVolumeText));
        OnPropertyChanged(nameof(VoiceMuteText));
    }

    /// <summary>
    /// Shows a stored volume; the caller applies it to the audio.
    /// </summary>
    /// <param name="percent">The volume in percent.</param>
    public void RestoreVoiceVolume(double percent)
    {
        _restoringVolume = true;
        try
        {
            VoiceVolume = percent;
        }
        finally
        {
            _restoringVolume = false;
        }
    }

    /// <summary>
    /// Applies a new voice volume.
    /// </summary>
    /// <param name="value">The volume in percent.</param>
    partial void OnVoiceVolumeChanged(double value)
    {
        // Louder than the original would only clip the voice, so 100 % is the top.
        var clamped = Math.Clamp(Math.Round(value), 0, MaxVoiceVolume);
        if (clamped != value)
        {
            VoiceVolume = clamped;
            return;
        }

        if (value > 0)
        {
            _lastAudibleVolume = value;
        }

        if (!_restoringVolume)
        {
            _voiceVolumeChanged?.Invoke(PeerId, value);
        }
    }

    /// <summary>
    /// Silences the voice on this device, or restores the last audible volume.
    /// </summary>
    [RelayCommand]
    private void ToggleVoiceMute() => VoiceVolume = IsVoiceMuted ? _lastAudibleVolume : 0;

    /// <summary>
    /// Restores the normal volume.
    /// </summary>
    [RelayCommand]
    private void ResetVoiceVolume() => VoiceVolume = MaxVoiceVolume;

    /// <summary>
    /// Gets or sets a value indicating whether the participant is speaking now.
    /// </summary>
    [ObservableProperty]
    public partial bool IsSpeaking { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the connection to the participant is weak, so they get the economy
    /// camera stream from this device.
    /// </summary>
    [ObservableProperty]
    public partial bool WeakConnection { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the microphone of the participant is off.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SwitchOffMicrophoneCommand))]
    public partial bool MicrophoneOff { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the camera of the participant is off.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SwitchOffCameraCommand))]
    public partial bool CameraOff { get; set; }

    /// <summary>
    /// Gets the avatar color.
    /// </summary>
    public string AvatarColor { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the tile is this device.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLocal { get; set; }

    /// <summary>
    /// Gets or sets the latest camera image (an Avalonia bitmap set by the view), or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCamera))]
    public partial object? CameraImage { get; set; }

    /// <summary>
    /// Gets a value indicating whether a camera image is shown instead of the avatar.
    /// </summary>
    public bool HasCamera => CameraImage is not null;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the first letter of the name.
    /// </summary>
    [ObservableProperty]
    public partial string Initial { get; set; } = "?";

    /// <summary>
    /// Gets or sets the role caption, for example <c>ведущий · вы</c>.
    /// </summary>
    [ObservableProperty]
    public partial string RoleText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the device fingerprint.
    /// </summary>
    [ObservableProperty]
    public partial string Fingerprint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the round trip text.
    /// </summary>
    [ObservableProperty]
    public partial string PingText { get; set; } = "—";

    /// <summary>
    /// Gets or sets the round trip level.
    /// </summary>
    [ObservableProperty]
    public partial IndicatorLevel PingLevel { get; set; }

    /// <summary>
    /// Gets or sets the drift text.
    /// </summary>
    [ObservableProperty]
    public partial string DriftText { get; set; } = "—";

    /// <summary>
    /// Gets or sets the drift level.
    /// </summary>
    [ObservableProperty]
    public partial IndicatorLevel DriftLevel { get; set; }

    /// <summary>
    /// Gets or sets the buffer text.
    /// </summary>
    [ObservableProperty]
    public partial string BufferText { get; set; } = "—";

    /// <summary>
    /// Gets or sets the buffer level.
    /// </summary>
    [ObservableProperty]
    public partial IndicatorLevel BufferLevel { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the participant waits for data.
    /// </summary>
    [ObservableProperty]
    public partial bool IsBuffering { get; set; }

    /// <summary>
    /// Gets or sets the position on the timeline, 0–1, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    public partial double? TimelineFraction { get; set; }

    /// <summary>
    /// Switches the microphone of the participant off. There is deliberately no command that switches it on.
    /// </summary>
    /// <returns>A task that completes when the request was sent.</returns>
    [RelayCommand(CanExecute = nameof(CanSwitchOffMicrophone))]
    private Task SwitchOffMicrophoneAsync() => _switchOff?.Invoke(PeerId, true, false) ?? Task.CompletedTask;

    /// <summary>
    /// Switches the camera of the participant off. There is deliberately no command that switches it on.
    /// </summary>
    /// <returns>A task that completes when the request was sent.</returns>
    [RelayCommand(CanExecute = nameof(CanSwitchOffCamera))]
    private Task SwitchOffCameraAsync() => _switchOff?.Invoke(PeerId, false, true) ?? Task.CompletedTask;

    /// <summary>
    /// Checks whether the microphone can be switched off.
    /// </summary>
    /// <returns><see langword="true"/> while it is on.</returns>
    private bool CanSwitchOffMicrophone() => !MicrophoneOff;

    /// <summary>
    /// Checks whether the camera can be switched off.
    /// </summary>
    /// <returns><see langword="true"/> while it is on.</returns>
    private bool CanSwitchOffCamera() => !CameraOff;

    /// <summary>
    /// Updates the tile.
    /// </summary>
    /// <param name="view">The participant view.</param>
    /// <param name="duration">The media duration, or <see langword="null"/>.</param>
    public void Update(ParticipantView view, TimeSpan? duration)
    {
        ArgumentNullException.ThrowIfNull(view);
        Name = view.Info.DisplayName;
        Initial = Name.Length > 0 ? char.ToUpperInvariant(Name[0]).ToString() : "?";
        IsLocal = view.IsLocal;
        RoleText = Texts.Get((view.Info.IsHost, view.IsLocal) switch
        {
            (true, true) => "Participant.Role.HostYou",
            (true, false) => "Participant.Role.Host",
            (false, true) => "Participant.Role.You",
            _ => view.Status?.UsesLocalCopy == true ? "Participant.Role.LocalCopy" : "Participant.Role.Participant",
        });
        Fingerprint = view.Info.PeerId.ToShortString();

        var status = view.Status;
        MicrophoneOff = status?.MicrophoneOff ?? false;
        CameraOff = status?.CameraOff ?? false;
        if (status is null)
        {
            PingText = DriftText = BufferText = "—";
            PingLevel = DriftLevel = BufferLevel = IndicatorLevel.Neutral;
            IsBuffering = false;
            TimelineFraction = null;
            return;
        }

        PingText = view.Info.IsHost ? "—" : DisplayFormat.Ping(status.RoundTripMilliseconds);
        PingLevel = view.Info.IsHost ? IndicatorLevel.Neutral : DisplayFormat.PingLevel(status.RoundTripMilliseconds);
        DriftText = DisplayFormat.Drift(status.Drift);
        DriftLevel = DisplayFormat.DriftLevel(status.Drift);
        BufferText = DisplayFormat.Buffer(status.CacheAhead);
        BufferLevel = DisplayFormat.BufferLevel(status.CacheAhead, status.IsBuffering);
        IsBuffering = status.IsBuffering && status.Position is not null;
        TimelineFraction = status.Position is { } position && duration is { TotalSeconds: > 0 } total
            ? Math.Clamp(position / total, 0, 1)
            : null;
    }
}
