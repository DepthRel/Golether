using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Core.Identity;
using Golether.Session;

namespace Golether.UI.ViewModels;

/// <summary>
/// A participant tile in the side panel.
/// </summary>
public sealed partial class ParticipantItemViewModel : ObservableObject
{
    /// <summary>
    /// Avatar colors, picked by the identifier.
    /// </summary>
    private static readonly string[] AvatarColors = ["#8FB8F0", "#E59BC4", "#B6D77A", "#F0A860", "#9DD6CF", "#C9A6F2"];

    /// <summary>
    /// Initializes a new instance of the <see cref="ParticipantItemViewModel"/> class.
    /// </summary>
    /// <param name="peerId">The participant.</param>
    /// <param name="switchOff">Switches devices of the participant off (microphone, camera); used by the host.</param>
    public ParticipantItemViewModel(PeerId peerId, Func<PeerId, bool, bool, Task>? switchOff = null)
    {
        _switchOff = switchOff;
        PeerId = peerId;
        AvatarColor = AvatarColors[Convert.ToInt32(peerId.Value[..2], 16) % AvatarColors.Length];
    }

    /// <summary>
    /// Switches devices of the participant off, or <see langword="null"/>.
    /// </summary>
    private readonly Func<PeerId, bool, bool, Task>? _switchOff;

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
    /// Gets or sets a value indicating whether the participant is speaking now.
    /// </summary>
    [ObservableProperty]
    public partial bool IsSpeaking { get; set; }

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
        RoleText = (view.Info.IsHost, view.IsLocal) switch
        {
            (true, true) => "ведущий · вы",
            (true, false) => "ведущий",
            (false, true) => "вы",
            _ => view.Status?.UsesLocalCopy == true ? "локальная копия" : "участник",
        };
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
