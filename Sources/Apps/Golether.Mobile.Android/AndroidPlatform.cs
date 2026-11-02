using Android;
using Android.Content;
using Android.Content.PM;
using Android.Hardware.Display;
using Android.Media;
using Android.Net;
using Android.Views;
using Golether.Media.Conference;
using Golether.Media.Conference.GStreamer.Android;
using Golether.Media.Player.Android;
using Golether.Mobile.Services;
using Golether.Security.Android;
using Golether.Security.Secrets;

namespace Golether.Mobile.Android;

/// <summary>
/// What the mobile client takes from Android.
/// </summary>
/// <param name="context">The application context.</param>
internal sealed class AndroidPlatform(Context context) : IMobilePlatform
{
    /// <summary>
    /// The number of the request for the camera and the microphone.
    /// </summary>
    public const int CAPTURE_REQUEST = 1;

    /// <summary>
    /// The outputs that stay private: with one of them the voices do not go to the loudspeaker.
    /// </summary>
    private static readonly AudioDeviceType[] _headsets =
    [
        AudioDeviceType.WiredHeadset,
        AudioDeviceType.WiredHeadphones,
        AudioDeviceType.UsbHeadset,
        AudioDeviceType.BluetoothA2dp,
        AudioDeviceType.BluetoothSco,
    ];

    /// <summary>
    /// The activity on the screen, or <see langword="null"/>.
    /// </summary>
    private Activity? _activity;

    /// <summary>
    /// Completes when the user has answered the request on the screen.
    /// </summary>
    private TaskCompletionSource? _answer;

    /// <summary>
    /// The hold on the sound of the phone while a session runs, or <see langword="null"/> outside one.
    /// </summary>
    private AudioFocusRequestClass? _audioFocus;

    /// <summary>
    /// Whether the camera on the back of the phone is chosen.
    /// </summary>
    private bool _backCamera;

    /// <summary>
    /// Follows the networks while a session runs, or <see langword="null"/> outside one.
    /// </summary>
    private ConnectivityManager.NetworkCallback? _networks;

    /// <inheritdoc />
    public event EventHandler? Returned;

    /// <inheritdoc />
    public event EventHandler<bool>? AudioInterrupted;

    /// <inheritdoc />
    public event EventHandler? NetworkReturned;

    /// <inheritdoc />
    public string DataDirectory => context.FilesDir!.AbsolutePath;

    /// <inheritdoc />
    public string CacheDirectory => context.CacheDir!.AbsolutePath;

    /// <inheritdoc />
    public ISecretProtector Secrets { get; } = new AndroidKeystoreSecretProtector();

    /// <inheritdoc />
    public bool CanSwitchCamera => PhoneCameras.HasBothSides;

    /// <inheritdoc />
    public bool IsOnScreen { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a session runs.
    /// </summary>
    public bool IsSessionActive => _audioFocus is not null;

    /// <summary>
    /// Remembers the activity on the screen; a request nobody can answer any more ends.
    /// </summary>
    /// <param name="activity">The activity, or <see langword="null"/> when it is gone.</param>
    public void Attach(Activity? activity)
    {
        _activity = activity;

        if (activity is null)
        {
            CompleteRequest();
        }
    }

    /// <summary>
    /// Tells the application that it is back on the screen.
    /// </summary>
    public void RaiseReturned() => Returned?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Notes whether the application is on the screen.
    /// </summary>
    /// <param name="onScreen">Whether it is.</param>
    public void SetOnScreen(bool onScreen) => IsOnScreen = onScreen;

    /// <summary>
    /// Takes the answer of the user to the request on the screen.
    /// </summary>
    public void CompleteRequest()
    {
        var answer = _answer;
        _answer = null;
        answer?.TrySetResult();
    }

    /// <inheritdoc />
    public void PrepareMedia() => MediaCodecSupport.Initialize();

    /// <inheritdoc />
    public void PrepareConference() => GStreamerAndroid.Initialize(context);

    /// <inheritdoc />
    public async Task<CaptureAccess> RequestCaptureAccessAsync(bool camera, bool microphone)
    {
        var wanted = new List<string>();
        if (camera && !IsAllowed(Manifest.Permission.Camera))
        {
            wanted.Add(Manifest.Permission.Camera);
        }

        if (microphone && !IsAllowed(Manifest.Permission.RecordAudio))
        {
            wanted.Add(Manifest.Permission.RecordAudio);
        }

        // The notification of a running session is asked for together with the devices, the first time they are.
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && camera && microphone && !IsAllowed(Manifest.Permission.PostNotifications))
        {
            wanted.Add(Manifest.Permission.PostNotifications);
        }

        if (wanted.Count > 0 && _activity is { } activity)
        {
            if (_answer is null)
            {
                _answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                activity.RequestPermissions([.. wanted], CAPTURE_REQUEST);
            }

            await _answer.Task;
        }

        return new CaptureAccess(IsAllowed(Manifest.Permission.Camera), IsAllowed(Manifest.Permission.RecordAudio));
    }

    /// <inheritdoc />
    public CaptureDevice? GetCamera()
    {
        var displays = (DisplayManager)context.GetSystemService(Context.DisplayService)!;
        var rotation = displays.GetDisplay(Display.DefaultDisplay)?.Rotation switch
        {
            SurfaceOrientation.Rotation90 => 90,
            SurfaceOrientation.Rotation180 => 180,
            SurfaceOrientation.Rotation270 => 270,
            _ => 0,
        };

        return PhoneCameras.Find(rotation, !_backCamera);
    }

    /// <inheritdoc />
    public void SwitchCamera() => _backCamera = !_backCamera;

    /// <inheritdoc />
    public void MoveToBackground() => _activity?.MoveTaskToBack(true);

    /// <inheritdoc />
    public void SetSessionActive(bool active)
    {
        var keeper = new Intent(context, typeof(SessionForegroundService));
        if (active)
        {
            context.StartForegroundService(keeper);
        }
        else
        {
            context.StopService(keeper);
        }

        if (_activity?.Window is { } window)
        {
            if (active)
            {
                window.AddFlags(WindowManagerFlags.KeepScreenOn);
            }
            else
            {
                window.ClearFlags(WindowManagerFlags.KeepScreenOn);
            }
        }

        // The voice stream of the conference plays from the earpiece by itself, and nobody holds a phone to the ear
        // while watching a film. A headset that is connected stays in charge.
        var audio = (AudioManager)context.GetSystemService(Context.AudioService)!;
        audio.Mode = active ? Mode.InCommunication : Mode.Normal;
        UseLoudspeaker(audio, active && !HasHeadset(audio));
        HoldSound(audio, active);
        FollowNetworks(active);
    }

    /// <summary>
    /// Starts or stops following the networks: when one comes back the way to the session need not wait for its turn.
    /// </summary>
    /// <param name="active">Whether a session runs.</param>
    private void FollowNetworks(bool active)
    {
        var networks = (ConnectivityManager)context.GetSystemService(Context.ConnectivityService)!;

        if (active && _networks is null)
        {
            _networks = new NetworkListener(() => NetworkReturned?.Invoke(this, EventArgs.Empty));
            networks.RegisterDefaultNetworkCallback(_networks);
        }
        else if (!active && _networks is { } listener)
        {
            networks.UnregisterNetworkCallback(listener);
            _networks = null;
        }
    }

    /// <summary>
    /// Changes the sound of the session by a step. A session runs in the mode of a call, where the system gives the keys
    /// to the voices only and asks nobody, and the film stays as loud as it was: the keys must move both.
    /// </summary>
    /// <param name="louder">Whether to make it louder.</param>
    public void ChangeVolume(bool louder)
    {
        var audio = (AudioManager)context.GetSystemService(Context.AudioService)!;
        var direction = louder ? Adjust.Raise : Adjust.Lower;

        audio.AdjustStreamVolume(global::Android.Media.Stream.VoiceCall, direction, 0);
        audio.AdjustStreamVolume(global::Android.Media.Stream.Music, direction, VolumeNotificationFlags.ShowUi);
    }

    /// <summary>
    /// Takes the sound of the phone for a session or gives it back: other players stop, and a call takes it away.
    /// </summary>
    /// <param name="audio">The audio service.</param>
    /// <param name="active">Whether a session runs.</param>
    private void HoldSound(AudioManager audio, bool active)
    {
        if (active && _audioFocus is null)
        {
            var attributes = new AudioAttributes.Builder().SetUsage(AudioUsageKind.VoiceCommunication)!.Build();
            _audioFocus = new AudioFocusRequestClass.Builder(AudioFocus.Gain)
                .SetAudioAttributes(attributes!)
                .SetOnAudioFocusChangeListener(new SoundListener(interrupted => AudioInterrupted?.Invoke(this, interrupted)))
                .Build();
            audio.RequestAudioFocus(_audioFocus!);
        }
        else if (!active && _audioFocus is { } focus)
        {
            audio.AbandonAudioFocusRequest(focus);
            _audioFocus = null;
        }
    }

    /// <summary>
    /// Sends the voices to the loudspeaker or back to where the system sends them.
    /// </summary>
    /// <param name="audio">The audio service.</param>
    /// <param name="loudspeaker">Whether to use the loudspeaker.</param>
    private static void UseLoudspeaker(AudioManager audio, bool loudspeaker)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(31))
        {
#pragma warning disable CA1422
            audio.SpeakerphoneOn = loudspeaker;
#pragma warning restore CA1422
            return;
        }

        var device = audio.AvailableCommunicationDevices.FirstOrDefault(d => d.Type == AudioDeviceType.BuiltinSpeaker);
        if (loudspeaker && device is not null)
        {
            audio.SetCommunicationDevice(device);
        }
        else
        {
            audio.ClearCommunicationDevice();
        }
    }

    /// <summary>
    /// Checks whether a headset is connected.
    /// </summary>
    /// <param name="audio">The audio service.</param>
    /// <returns><see langword="true"/> when there is one.</returns>
    private static bool HasHeadset(AudioManager audio)
        => audio.GetDevices(GetDevicesTargets.Outputs)!.Any(device => _headsets.Contains(device.Type));

    /// <summary>
    /// Checks whether a permission was given.
    /// </summary>
    /// <param name="permission">The permission.</param>
    /// <returns><see langword="true"/> when it was.</returns>
    private bool IsAllowed(string permission) => context.CheckSelfPermission(permission) == Permission.Granted;

    /// <summary>
    /// Tells when the phone has a network to use.
    /// </summary>
    /// <param name="available">Told about a network.</param>
    private sealed class NetworkListener(Action available) : ConnectivityManager.NetworkCallback
    {
        /// <inheritdoc />
        public override void OnAvailable(Network network) => available();
    }

    /// <summary>
    /// Follows who holds the sound of the phone.
    /// </summary>
    /// <param name="changed">Told whether the sound was taken away.</param>
    private sealed class SoundListener(Action<bool> changed) : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
    {
        /// <inheritdoc />
        public void OnAudioFocusChange(AudioFocus focusChange)
        {
            if (focusChange is AudioFocus.Gain)
            {
                changed(false);
            }
            else if (focusChange is AudioFocus.Loss or AudioFocus.LossTransient)
            {
                changed(true);
            }
        }
    }
}
