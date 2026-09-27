using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Localization;
using Golether.UI.Services;

namespace Golether.UI.ViewModels.Dialogs;

/// <summary>
/// The settings window: language, avatar, camera and microphone, and AmneziaWG tunnels, gathered from the pieces the
/// main window already owns.
/// </summary>
/// <remarks>
/// Nothing is applied while the window is open: the choices are held here and handed over by <see cref="ApplyAsync"/>,
/// so closing with "cancel" leaves the previous avatar, language and devices in place.
/// </remarks>
public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>
    /// Opens the avatar chooser and returns the chosen avatar, without storing it.
    /// </summary>
    private readonly Func<Task<AvatarSelection?>> _pickAvatar;

    /// <summary>
    /// Stores an avatar and sends it to the session.
    /// </summary>
    private readonly Func<AvatarSelection, Task> _applyAvatar;

    /// <summary>
    /// Opens the tunnel dialog.
    /// </summary>
    private readonly Func<Task> _openTunnels;

    /// <summary>
    /// The avatar waiting to be applied, or <see langword="null"/> when it was not changed.
    /// </summary>
    private AvatarSelection? _pendingAvatar;

    /// <summary>
    /// Whether a device choice is being copied from the device list rather than picked by the user.
    /// </summary>
    private bool _syncingDevices;

    /// <summary>
    /// Whether the user picked a camera, so the list must no longer overwrite the choice.
    /// </summary>
    private bool _cameraTouched;

    /// <summary>
    /// Whether the user picked a microphone.
    /// </summary>
    private bool _microphoneTouched;

    /// <summary>
    /// The camera the boxes last held, put back when a list update drops the selection.
    /// </summary>
    private DeviceOption? _lastCamera;

    /// <summary>
    /// The microphone the boxes last held.
    /// </summary>
    private DeviceOption? _lastMicrophone;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
    /// </summary>
    /// <param name="language">The language selector, or <see langword="null"/> when the language cannot be chosen.</param>
    /// <param name="devices">The camera and microphone chooser, or <see langword="null"/> when none is available.</param>
    /// <param name="conferenceAvailable">Whether cameras and voices work.</param>
    /// <param name="conferenceNotice">Why cameras and voices do not work, when they do not.</param>
    /// <param name="canChooseAvatar">Whether choosing an avatar is offered.</param>
    /// <param name="canConfigureTunnels">Whether tunnels can be configured now (not during a session).</param>
    /// <param name="avatarPreview">The current avatar image, or <see langword="null"/>.</param>
    /// <param name="pickAvatar">Opens the avatar chooser and returns the chosen avatar without storing it.</param>
    /// <param name="applyAvatar">Stores an avatar and sends it to the session.</param>
    /// <param name="openTunnels">Opens the tunnel dialog.</param>
    public SettingsViewModel(
        LanguageViewModel? language,
        DevicesViewModel? devices,
        bool conferenceAvailable,
        string conferenceNotice,
        bool canChooseAvatar,
        bool canConfigureTunnels,
        object? avatarPreview,
        Func<Task<AvatarSelection?>> pickAvatar,
        Func<AvatarSelection, Task> applyAvatar,
        Func<Task> openTunnels)
    {
        Language = language;
        Devices = devices;
        ConferenceAvailable = conferenceAvailable;
        ConferenceNotice = conferenceNotice;
        CanChooseAvatar = canChooseAvatar;
        CanConfigureTunnels = canConfigureTunnels;
        AvatarPreview = avatarPreview;
        _pickAvatar = pickAvatar ?? throw new ArgumentNullException(nameof(pickAvatar));
        _applyAvatar = applyAvatar ?? throw new ArgumentNullException(nameof(applyAvatar));
        _openTunnels = openTunnels ?? throw new ArgumentNullException(nameof(openTunnels));
        SelectedLanguage = language?.Selected;
        SyncDevices();
    }

    /// <summary>
    /// Gets the language selector, or <see langword="null"/> when the language cannot be chosen.
    /// </summary>
    public LanguageViewModel? Language { get; }

    /// <summary>
    /// Gets a value indicating whether the language can be chosen.
    /// </summary>
    public bool HasLanguage => Language is not null;

    /// <summary>
    /// Gets the camera and microphone chooser, or <see langword="null"/> when none is available.
    /// </summary>
    public DevicesViewModel? Devices { get; }

    /// <summary>
    /// Gets a value indicating whether cameras and voices work.
    /// </summary>
    public bool ConferenceAvailable { get; }

    /// <summary>
    /// Gets the notice about why cameras and voices do not work.
    /// </summary>
    public string ConferenceNotice { get; }

    /// <summary>
    /// Gets a value indicating whether choosing an avatar is offered.
    /// </summary>
    public bool CanChooseAvatar { get; }

    /// <summary>
    /// Gets a value indicating whether tunnels can be configured now. Tunnels are locked during a session.
    /// </summary>
    public bool CanConfigureTunnels { get; }

    /// <summary>
    /// Gets or sets the language chosen in the window; it is applied only by <see cref="ApplyAsync"/>.
    /// </summary>
    [ObservableProperty]
    public partial LanguageOption? SelectedLanguage { get; set; }

    /// <summary>
    /// Gets or sets the camera chosen in the window; it is applied only by <see cref="ApplyAsync"/>.
    /// </summary>
    [ObservableProperty]
    public partial DeviceOption? SelectedCamera { get; set; }

    /// <summary>
    /// Gets or sets the microphone chosen in the window; it is applied only by <see cref="ApplyAsync"/>.
    /// </summary>
    [ObservableProperty]
    public partial DeviceOption? SelectedMicrophone { get; set; }

    /// <summary>
    /// Gets or sets the avatar shown in the window: the one waiting to be applied, or the current one.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvatar))]
    public partial object? AvatarPreview { get; set; }

    /// <summary>
    /// Gets a value indicating whether an avatar is shown.
    /// </summary>
    public bool HasAvatar => AvatarPreview is not null;

    /// <summary>
    /// Gets the window title.
    /// </summary>
    public string Title => Texts.Get("Settings.Title");

    /// <summary>
    /// Takes the camera the user picked. An empty value never comes from the user: a box drops its selection while its
    /// list is being brought up to date, so the camera that was chosen is put straight back — unless it really has
    /// gone from the list, and then the empty value stands.
    /// </summary>
    /// <param name="value">The new value.</param>
    partial void OnSelectedCameraChanged(DeviceOption? value)
    {
        if (_syncingDevices)
        {
            return;
        }

        if (value is null)
        {
            Restore(ref _lastCamera, option => SelectedCamera = option, Devices?.Cameras);
            return;
        }

        _lastCamera = value;
        _cameraTouched = true;
    }

    /// <summary>
    /// Takes the microphone the user picked, keeping it through a list update in the same way.
    /// </summary>
    /// <param name="value">The new value.</param>
    partial void OnSelectedMicrophoneChanged(DeviceOption? value)
    {
        if (_syncingDevices)
        {
            return;
        }

        if (value is null)
        {
            Restore(ref _lastMicrophone, option => SelectedMicrophone = option, Devices?.Microphones);
            return;
        }

        _lastMicrophone = value;
        _microphoneTouched = true;
    }

    /// <summary>
    /// Puts a dropped choice back when its device is still on offer; a device that is gone stays gone.
    /// </summary>
    /// <param name="last">The choice that was there before.</param>
    /// <param name="restore">Writes the choice back.</param>
    /// <param name="offered">The devices on offer.</param>
    private void Restore(ref DeviceOption? last, Action<DeviceOption> restore, IEnumerable<DeviceOption>? offered)
    {
        if (last is not { } previous || offered?.Contains(previous) != true)
        {
            last = null;
            return;
        }

        _syncingDevices = true;
        try
        {
            restore(previous);
        }
        finally
        {
            _syncingDevices = false;
        }
    }

    /// <summary>
    /// Shows the device choice of the finished device list, unless the user already picked something. The list is
    /// read after the window opened, and refilling it empties the boxes for a moment, so the choice is restored here
    /// once the list is complete.
    /// </summary>
    public void SyncDevices()
    {
        if (Devices is null)
        {
            return;
        }

        _syncingDevices = true;
        try
        {
            if (!_cameraTouched && Devices.SelectedCamera is { } camera)
            {
                SelectedCamera = camera;
                _lastCamera = camera;
            }

            if (!_microphoneTouched && Devices.SelectedMicrophone is { } microphone)
            {
                SelectedMicrophone = microphone;
                _lastMicrophone = microphone;
            }
        }
        finally
        {
            _syncingDevices = false;
        }
    }

    /// <summary>
    /// Opens the avatar chooser and shows the result; nothing is stored until the settings are applied.
    /// </summary>
    /// <returns>A task that completes when the chooser is closed.</returns>
    [RelayCommand]
    private async Task ChangeAvatarAsync()
    {
        if (await _pickAvatar() is not { } selection)
        {
            return;
        }

        _pendingAvatar = selection;
        AvatarPreview = Decode(selection.Avatar) ?? AvatarPreview;
    }

    /// <summary>
    /// Opens the tunnel dialog. Tunnels bring themselves up, so this is not part of the applied settings.
    /// </summary>
    /// <returns>A task that completes when the dialog is closed.</returns>
    [RelayCommand]
    private Task OpenTunnelsAsync() => _openTunnels();

    /// <summary>
    /// Hands the chosen settings over: the avatar first, then the devices, and the language last, so the window is
    /// already done when every text changes.
    /// </summary>
    /// <returns>A task that completes when everything is applied.</returns>
    public async Task ApplyAsync()
    {
        if (_pendingAvatar is { } avatar)
        {
            _pendingAvatar = null;
            await _applyAvatar(avatar);
        }

        if (Devices is not null)
        {
            if (_cameraTouched && SelectedCamera is { } camera && !ReferenceEquals(camera, Devices.SelectedCamera))
            {
                Devices.SelectedCamera = camera;
            }

            if (_microphoneTouched && SelectedMicrophone is { } microphone && !ReferenceEquals(microphone, Devices.SelectedMicrophone))
            {
                Devices.SelectedMicrophone = microphone;
            }
        }

        if (Language is { } language && SelectedLanguage is { } option && option.Code != language.Selected.Code)
        {
            language.Selected = option;
        }
    }

    /// <summary>
    /// Decodes avatar bytes for the preview.
    /// </summary>
    /// <param name="bytes">The PNG bytes.</param>
    /// <returns>The bitmap, or <see langword="null"/> when the bytes cannot be read.</returns>
    private static object? Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A preview that cannot be drawn must never keep the settings window from opening or applying.
            return null;
        }
    }
}
