using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Localization;
using Golether.Core.Data.Stores;
using Golether.Media.Player;

namespace Golether.UI.ViewModels;

/// <summary>
/// The sound level of the player on this device. It never affects other participants.
/// </summary>
/// <remarks>
/// A volume of 0 means muted. Unmuting at 0 restores the last audible volume; moving the slider above 0 unmutes.
/// </remarks>
public sealed partial class VolumeViewModel : ObservableObject
{
    /// <summary>
    /// The setting with the volume.
    /// </summary>
    public const string VolumeSetting = "player.volume";

    /// <summary>
    /// The setting with the mute state.
    /// </summary>
    public const string MutedSetting = "player.muted";

    /// <summary>
    /// The volume restored when unmuting at 0.
    /// </summary>
    private const double DefaultAudibleVolume = 50;

    /// <summary>
    /// The player.
    /// </summary>
    private readonly ILocalPlayerControls _player;

    /// <summary>
    /// The settings.
    /// </summary>
    private readonly ISettingsStore _settings;

    /// <summary>
    /// The last volume above 0.
    /// </summary>
    private double _lastAudible = 100;

    /// <summary>
    /// Whether stored settings are being applied.
    /// </summary>
    private bool _loading;

    /// <summary>
    /// Initializes a new instance of the <see cref="VolumeViewModel"/> class.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="settings">The settings.</param>
    public VolumeViewModel(ILocalPlayerControls player, ISettingsStore settings)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// Gets or sets the volume, 0–100.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Level), nameof(Caption), nameof(IsSilent), nameof(IsQuiet), nameof(IsLoud))]
    public partial double Volume { get; set; } = 100;

    /// <summary>
    /// Gets or sets a value indicating whether the sound is off.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Level), nameof(Caption), nameof(IsSilent), nameof(IsQuiet), nameof(IsLoud))]
    public partial bool IsMuted { get; set; }

    /// <summary>
    /// Gets the loudness shown by the speaker icon: 0 muted, 1 quiet, 2 loud.
    /// </summary>
    public int Level => IsMuted || Volume <= 0 ? 0 : Volume < 50 ? 1 : 2;

    /// <summary>
    /// Gets a value indicating whether the speaker icon shows no sound.
    /// </summary>
    public bool IsSilent => Level == 0;

    /// <summary>
    /// Gets a value indicating whether the speaker icon shows a quiet sound.
    /// </summary>
    public bool IsQuiet => Level == 1;

    /// <summary>
    /// Gets a value indicating whether the speaker icon shows a loud sound.
    /// </summary>
    public bool IsLoud => Level == 2;

    /// <summary>
    /// Gets the text of the volume, for example <c>75 %</c> or <c>muted</c>.
    /// </summary>
    public string Caption => IsMuted
        ? Texts.Get("Volume.Muted")
        : Texts.Format("Format.Percent", Math.Round(Volume).ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Tells the view that the texts changed with the language.
    /// </summary>
    public void RefreshTexts() => OnPropertyChanged(nameof(Caption));

    /// <summary>
    /// Loads the stored volume and applies it.
    /// </summary>
    /// <returns>A task that completes when the volume is applied.</returns>
    public async Task LoadAsync()
    {
        var volume = await _settings.GetAsync(VolumeSetting, CancellationToken.None).ConfigureAwait(true);
        var muted = await _settings.GetAsync(MutedSetting, CancellationToken.None).ConfigureAwait(true);
        _loading = true;
        try
        {
            if (double.TryParse(volume, NumberStyles.Float, CultureInfo.InvariantCulture, out var stored))
            {
                Volume = Math.Clamp(stored, 0, 100);
            }

            IsMuted = muted == "true" || Volume <= 0;
        }
        finally
        {
            _loading = false;
        }

        Apply();
    }

    /// <summary>
    /// Mutes, or unmutes and restores an audible volume.
    /// </summary>
    [RelayCommand]
    private void ToggleMute()
    {
        if (!IsMuted)
        {
            IsMuted = true;
            return;
        }

        if (Volume <= 0)
        {
            Volume = _lastAudible > 0 ? _lastAudible : DefaultAudibleVolume;
        }

        IsMuted = false;
    }

    /// <summary>
    /// Keeps the mute state in line with the volume.
    /// </summary>
    /// <param name="oldValue">The previous volume.</param>
    /// <param name="newValue">The new volume.</param>
    partial void OnVolumeChanged(double oldValue, double newValue)
    {
        if (newValue > 0)
        {
            _lastAudible = newValue;
        }

        if (!_loading)
        {
            // Reaching 0 mutes; any other change of the level makes the sound audible again.
            if (newValue <= 0)
            {
                IsMuted = true;
            }
            else if (IsMuted && Math.Abs(newValue - oldValue) > double.Epsilon)
            {
                IsMuted = false;
            }
        }

        Save();
    }

    /// <summary>
    /// Applies and stores the mute state.
    /// </summary>
    /// <param name="value">The new state.</param>
    partial void OnIsMutedChanged(bool value) => Save();

    /// <summary>
    /// Applies the state to the player and stores it.
    /// </summary>
    private void Save()
    {
        if (_loading)
        {
            return;
        }

        Apply();
        _ = _settings.SetAsync(VolumeSetting, Math.Round(Volume).ToString(CultureInfo.InvariantCulture), CancellationToken.None);
        _ = _settings.SetAsync(MutedSetting, IsMuted ? "true" : "false", CancellationToken.None);
    }

    /// <summary>
    /// Applies the state to the player.
    /// </summary>
    private void Apply()
    {
        _player.SetVolume(Volume);
        _player.SetMuted(IsMuted || Volume <= 0);
    }
}
