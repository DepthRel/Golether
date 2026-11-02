using System.Runtime.Versioning;
using Android;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Golether.Localization;

namespace Golether.Mobile.Android;

/// <summary>
/// Keeps a session alive off the screen: without a foreground service Android cuts the network and the sound of an
/// application a few seconds after the user leaves it, and the session breaks.
/// </summary>
[Service(
    Name = "depthrel.golether.SessionForegroundService",
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeMediaPlayback | ForegroundService.TypeMicrophone)]
internal sealed class SessionForegroundService : Service
{
    /// <summary>
    /// The number of the notification.
    /// </summary>
    private const int NOTIFICATION_ID = 1;

    /// <summary>
    /// The notification channel.
    /// </summary>
    private const string CHANNEL_ID = "session";

    /// <summary>
    /// The colour of the mark in the list of notifications: the amber of the logo.
    /// </summary>
    private const int LAMP_COLOR = unchecked((int)0xFFF0A860);

    /// <inheritdoc />
    public override IBinder? OnBind(Intent? intent) => null;

    /// <inheritdoc />
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var notification = CreateNotification();

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            StartForeground(NOTIFICATION_ID, notification, AllowedTypes());
        }
        else
        {
            StartForeground(NOTIFICATION_ID, notification);
        }

        return StartCommandResult.NotSticky;
    }

    /// <summary>
    /// Works out what the service holds on to: the system refuses the microphone without the permission for it.
    /// </summary>
    /// <returns>The types of the service.</returns>
    [SupportedOSPlatform("android29.0")]
    private ForegroundService AllowedTypes()
        => CheckSelfPermission(Manifest.Permission.RecordAudio) == Permission.Granted
            ? ForegroundService.TypeMediaPlayback | ForegroundService.TypeMicrophone
            : ForegroundService.TypeMediaPlayback;

    /// <summary>
    /// Creates the notification that says a session runs and leads back to it.
    /// </summary>
    /// <returns>The notification.</returns>
    private Notification CreateNotification()
    {
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(new NotificationChannel(CHANNEL_ID, Texts.Get("Mobile.Session.Channel"), NotificationImportance.Low));

        var open = new Intent(this, typeof(MainActivity));
        var returning = PendingIntent.GetActivity(this, 0, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        return new Notification.Builder(this, CHANNEL_ID)
            .SetContentTitle(Texts.Get("Mobile.Session.Running"))
            .SetSmallIcon(Resource.Drawable.ic_notification)
            .SetColor(LAMP_COLOR)
            .SetContentIntent(returning)
            .SetOngoing(true)
            .Build();
    }
}
