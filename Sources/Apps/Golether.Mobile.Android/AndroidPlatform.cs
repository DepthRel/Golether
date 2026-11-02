using Android;
using Android.Content;
using Android.Content.PM;
using Android.Hardware.Display;
using Android.Media;
using Android.Net;
using Android.Views;
using Golether.Client.Services;
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

    /// <summary>
    /// Follows how the device is held after the film left the whole screen, or <see langword="null"/> before that.
    /// </summary>
    private TurnListener? _turns;

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

    /// <inheritdoc />
    public ISoundPlayer Sounds { get; } = new AndroidSoundPlayer(context);

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
    public void SetSessionActive(bool active)
    {
        var keeper = new Intent(context, typeof(SessionForegroundService));
        // A service asked to come to the foreground must get there before it stops, or the system ends the application:
        // one that has not started yet stops itself as soon as it has.
        SessionForegroundService.IsWanted = active;
        if (active)
        {
            context.StartForegroundService(keeper);
        }
        else if (SessionForegroundService.IsInForeground)
        {
            context.StopService(keeper);
        }

        if (_activity is { Window: { } window } activity)
        {
            if (active)
            {
                window.AddFlags(WindowManagerFlags.KeepScreenOn);
            }
            else
            {
                window.ClearFlags(WindowManagerFlags.KeepScreenOn);
            }

            // The film and the voices play as media, with no call around them: the keys of the phone move the volume of
            // media only, even in a silence, and the sound stays where media plays (the headphones of the user).
            activity.VolumeControlStream = active ? global::Android.Media.Stream.Music : (global::Android.Media.Stream)AudioManager.UseDefaultStreamType;
        }

        var audio = (AudioManager)context.GetSystemService(Context.AudioService)!;
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

    /// <inheritdoc />
    public string? ReadClipboardText()
    {
        // The clipboard of Avalonia reads plain text only, and a link copied from a messenger or a browser is often
        // kept as a link or a page: the system turns any of them into text.
        var clipboard = (ClipboardManager?)context.GetSystemService(Context.ClipboardService);
        if (clipboard?.PrimaryClip is not { ItemCount: > 0 } clip)
        {
            return null;
        }

        return clip.GetItemAt(0)?.CoerceToText(context);
    }

    /// <inheritdoc />
    public void HoldLandscape(bool hold)
    {
        if (_activity is not { } activity)
        {
            return;
        }

        _turns?.Disable();

        // On the whole screen the film stays on its side (either side, as the device is turned) until the user takes
        // it back; turning the device upright does not end it.
        if (hold)
        {
            activity.RequestedOrientation = ScreenOrientation.SensorLandscape;
            return;
        }

        // Taken back, the screen stands upright and follows the device again once the device stands upright too: let go
        // at once, it would fall back on its side in the hands that still hold the device so.
        if (!TurnsByItself())
        {
            activity.RequestedOrientation = ScreenOrientation.Unspecified;
            return;
        }

        activity.RequestedOrientation = ScreenOrientation.SensorPortrait;
        _turns ??= new TurnListener(context, OnDeviceTurned);
        if (_turns.CanDetectOrientation())
        {
            _turns.Enable();
        }
        else
        {
            activity.RequestedOrientation = ScreenOrientation.Unspecified;
        }
    }

    /// <summary>
    /// Lets the screen follow the device again once the device is held upright after the film left the whole screen.
    /// </summary>
    /// <param name="degrees">How far the device is turned from upright, or <see cref="OrientationEventListener.OrientationUnknown"/>.</param>
    private void OnDeviceTurned(int degrees)
    {
        var upright = degrees is (>= 0 and < 30) or > 330 or (> 150 and < 210);
        if (!upright)
        {
            return;
        }

        _turns?.Disable();
        if (_activity is { RequestedOrientation: ScreenOrientation.SensorPortrait } activity)
        {
            activity.RequestedOrientation = ScreenOrientation.Unspecified;
        }
    }

    /// <summary>
    /// Checks whether the user lets the screen turn with the device.
    /// </summary>
    /// <returns><see langword="true"/> when it turns.</returns>
    private bool TurnsByItself()
        => global::Android.Provider.Settings.System.GetInt(context.ContentResolver, global::Android.Provider.Settings.System.AccelerometerRotation, 0) == 1;

    /// <summary>
    /// Takes the sound of the phone for a session or gives it back: other players stop, and a call takes it away.
    /// </summary>
    /// <param name="audio">The audio service.</param>
    /// <param name="active">Whether a session runs.</param>
    private void HoldSound(AudioManager audio, bool active)
    {
        if (active && _audioFocus is null)
        {
            var attributes = new AudioAttributes.Builder().SetUsage(AudioUsageKind.Media)!.SetContentType(AudioContentType.Movie)!.Build();
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
    /// Tells how far the device is turned from upright.
    /// </summary>
    /// <param name="context">The application context.</param>
    /// <param name="turned">Told the angle in degrees.</param>
    private sealed class TurnListener(Context context, Action<int> turned) : OrientationEventListener(context)
    {
        /// <inheritdoc />
        public override void OnOrientationChanged(int orientation) => turned(orientation);
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
