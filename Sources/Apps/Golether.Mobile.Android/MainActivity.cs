using Android.Content;
using Android.Content.PM;
using Android.Views;
using Avalonia.Android;

namespace Golether.Mobile.Android;

/// <summary>
/// The only activity: the system does not recreate it on a turn of the screen, the view adapts by itself.
/// </summary>
[Activity(
    Name = "depthrel.golether.MainActivity",
    Label = "Golether",
    Theme = "@style/Theme.Golether",
    Icon = "@mipmap/ic_launcher",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.UiMode)]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataScheme = "golether")]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataScheme = "https", DataHost = "depthrel.github.io", DataPathPrefix = "/Golether/join")]
public sealed class MainActivity : AvaloniaMainActivity
{
    /// <summary>
    /// Gets the system services of the application.
    /// </summary>
    private static AndroidPlatform Platform => (AndroidPlatform)App.Platform!;

    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Platform.Attach(this);
        OpenLink(Intent);

#if DEVICE_CHECKS
        if (Intent is { } intent && intent.GetBooleanExtra("golether.checks", false))
        {
            Diagnostics.DeviceChecks.Start(ApplicationContext!, intent);
        }
#endif
    }

    /// <inheritdoc />
    protected override void OnDestroy()
    {
        Platform.Attach(null);
        base.OnDestroy();
    }

    /// <inheritdoc />
    protected override void OnStart()
    {
        base.OnStart();
        Platform.SetOnScreen(true);
    }

    /// <inheritdoc />
    protected override void OnStop()
    {
        Platform.SetOnScreen(false);
        base.OnStop();
    }

    /// <inheritdoc />
    protected override void OnRestart()
    {
        base.OnRestart();
        Platform.RaiseReturned();
    }

    /// <inheritdoc />
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        OpenLink(intent);
    }

    /// <inheritdoc />
    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e is { KeyCode: Keycode.VolumeUp or Keycode.VolumeDown } && Platform.IsSessionActive)
        {
            if (e.Action == KeyEventActions.Down)
            {
                Platform.ChangeVolume(e.KeyCode == Keycode.VolumeUp);
            }

            return true;
        }

        return base.DispatchKeyEvent(e);
    }

    /// <inheritdoc />
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode == AndroidPlatform.CAPTURE_REQUEST)
        {
            Platform.CompleteRequest();
        }
    }

    /// <summary>
    /// Hands an invitation that opened the application over to the join screen.
    /// </summary>
    /// <param name="intent">The intent.</param>
    private static void OpenLink(Intent? intent)
    {
        if (intent?.Action == Intent.ActionView && intent.DataString is { Length: > 0 } link)
        {
            App.OpenInvite(link);
        }
    }
}
