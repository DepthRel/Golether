using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace Golether.Mobile.Android;

/// <summary>
/// The Android application: starts Avalonia with the screens of the mobile client.
/// </summary>
[Application]
public sealed class AndroidApp : AvaloniaAndroidApplication<App>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AndroidApp"/> class; called by the Android runtime.
    /// </summary>
    /// <param name="javaReference">The Java object.</param>
    /// <param name="transfer">Who owns the reference.</param>
    public AndroidApp(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    /// <inheritdoc />
    public override void OnCreate()
    {
        App.Platform = new AndroidPlatform(this);
        base.OnCreate();
    }

    /// <inheritdoc />
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        => base.CustomizeAppBuilder(builder).WithInterFont();
}
