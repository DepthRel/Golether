using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Core.Data.Enums;
using Golether.Core.Data.Stores;
using Golether.Localization;
using Golether.Media.Conference;

namespace Golether.UI.ViewModels;

/// <summary>
/// The camera and microphone choice of the side panel.
/// </summary>
public sealed partial class DevicesViewModel : ObservableObject
{
    /// <summary>
    /// The setting with the camera identifier.
    /// </summary>
    public const string CameraSetting = "conference.camera";

    /// <summary>
    /// The setting with the microphone identifier.
    /// </summary>
    public const string MicrophoneSetting = "conference.microphone";

    /// <summary>
    /// The devices backend.
    /// </summary>
    private readonly ICaptureDeviceSelector _selector;

    /// <summary>
    /// The settings.
    /// </summary>
    private readonly ISettingsStore _settings;

    /// <summary>
    /// Whether list updates are being applied (selection changes are not user choices then).
    /// </summary>
    private bool _updating;

    /// <summary>
    /// Initializes a new instance of the <see cref="DevicesViewModel"/> class.
    /// </summary>
    /// <param name="selector">The devices backend.</param>
    /// <param name="settings">The settings.</param>
    public DevicesViewModel(ICaptureDeviceSelector selector, ISettingsStore settings)
    {
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _updating = true;
        SelectedCamera = DeviceOption.SystemDefault;
        SelectedMicrophone = DeviceOption.SystemDefault;
        _updating = false;
    }

    /// <summary>
    /// Gets the cameras.
    /// </summary>
    public ObservableCollection<DeviceOption> Cameras { get; } = [DeviceOption.SystemDefault];

    /// <summary>
    /// Gets the microphones.
    /// </summary>
    public ObservableCollection<DeviceOption> Microphones { get; } = [DeviceOption.SystemDefault];

    /// <summary>
    /// Gets or sets the chosen camera.
    /// </summary>
    [ObservableProperty]
    public partial DeviceOption? SelectedCamera { get; set; }

    /// <summary>
    /// Gets or sets the chosen microphone.
    /// </summary>
    [ObservableProperty]
    public partial DeviceOption? SelectedMicrophone { get; set; }

    /// <summary>
    /// Gets or sets a message about the last failure, or an empty string.
    /// </summary>
    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    /// <summary>
    /// Re-reads the device lists and restores the saved choice.
    /// </summary>
    /// <returns>A task that completes when the lists are updated.</returns>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!_selector.IsAvailable)
        {
            return;
        }

        IReadOnlyList<CaptureDevice> devices;
        try
        {
            devices = await Task.Run(_selector.GetDevices).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Error = Texts.Format("Devices.Error.List", ex.Message);
            return;
        }

        var cameraId = await _settings.GetAsync(CameraSetting, CancellationToken.None).ConfigureAwait(true);
        var microphoneId = await _settings.GetAsync(MicrophoneSetting, CancellationToken.None).ConfigureAwait(true);
        _updating = true;
        try
        {
            SelectedCamera = Fill(Cameras, devices, CaptureDeviceKind.Camera, cameraId);
            SelectedMicrophone = Fill(Microphones, devices, CaptureDeviceKind.Microphone, microphoneId);
        }
        finally
        {
            _updating = false;
        }

        await ApplyAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Saves and applies a new camera.
    /// </summary>
    /// <param name="value">The camera.</param>
    partial void OnSelectedCameraChanged(DeviceOption? value)
    {
        if (!_updating && value is not null)
        {
            _ = SaveAndApplyAsync(CameraSetting, value);
        }
    }

    /// <summary>
    /// Saves and applies a new microphone.
    /// </summary>
    /// <param name="value">The microphone.</param>
    partial void OnSelectedMicrophoneChanged(DeviceOption? value)
    {
        if (!_updating && value is not null)
        {
            _ = SaveAndApplyAsync(MicrophoneSetting, value);
        }
    }

    /// <summary>
    /// Replaces the entries of a list and returns the entry to select.
    /// </summary>
    /// <param name="target">The list.</param>
    /// <param name="devices">All devices.</param>
    /// <param name="kind">The kind for this list.</param>
    /// <param name="savedId">The saved device identifier.</param>
    /// <returns>The saved device, or the system default when it is absent.</returns>
    private static DeviceOption Fill(ObservableCollection<DeviceOption> target, IReadOnlyList<CaptureDevice> devices, CaptureDeviceKind kind, string? savedId)
    {
        target.Clear();
        target.Add(DeviceOption.SystemDefault);
        foreach (var device in devices.Where(d => d.Kind == kind).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            target.Add(new DeviceOption(device.Name, device));
        }

        return target.FirstOrDefault(o => o.Device is not null && o.Device.Id == savedId) ?? DeviceOption.SystemDefault;
    }

    /// <summary>
    /// Saves a choice and switches capture to it.
    /// </summary>
    /// <param name="setting">The setting.</param>
    /// <param name="option">The chosen entry.</param>
    /// <returns>A task that completes when the choice is applied.</returns>
    private async Task SaveAndApplyAsync(string setting, DeviceOption option)
    {
        await _settings.SetAsync(setting, option.Device?.Id ?? string.Empty, CancellationToken.None).ConfigureAwait(true);
        await ApplyAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Passes the current choice to capture.
    /// </summary>
    /// <returns>A task that completes when capture uses the devices.</returns>
    private async Task ApplyAsync()
    {
        try
        {
            await _selector.SelectDevicesAsync(SelectedCamera?.Device, SelectedMicrophone?.Device, CancellationToken.None).ConfigureAwait(true);
            Error = string.Empty;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Error = Texts.Format("Devices.Error.Switch", ex.Message);
        }
    }
}
