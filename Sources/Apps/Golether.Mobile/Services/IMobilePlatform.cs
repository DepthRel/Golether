using Golether.Client.Services;
using Golether.Media.Conference;
using Golether.Security.Secrets;

namespace Golether.Mobile.Services;

/// <summary>
/// What the mobile client takes from the system it runs on.
/// </summary>
public interface IMobilePlatform
{
    /// <summary>
    /// Raised when the application comes back to the screen after it was away.
    /// </summary>
    event EventHandler? Returned;

    /// <summary>
    /// Raised on any thread when something more urgent, such as a call, takes the sound away, with
    /// <see langword="true"/>, and when it comes back, with <see langword="false"/>.
    /// </summary>
    event EventHandler<bool>? AudioInterrupted;

    /// <summary>
    /// Raised on any thread when a network became available again.
    /// </summary>
    event EventHandler? NetworkReturned;

    /// <summary>
    /// Gets the folder of the data of the application.
    /// </summary>
    string DataDirectory { get; }

    /// <summary>
    /// Gets the folder of the files the system may delete.
    /// </summary>
    string CacheDirectory { get; }

    /// <summary>
    /// Gets the protection of secrets on this system.
    /// </summary>
    ISecretProtector Secrets { get; }

    /// <summary>
    /// Gets a value indicating whether there is a camera on each side of the device.
    /// </summary>
    bool CanSwitchCamera { get; }

    /// <summary>
    /// Gets a value indicating whether the application is on the screen; off it the system takes the camera away.
    /// </summary>
    bool IsOnScreen { get; }

    /// <summary>
    /// Prepares the native media libraries; called once before the first player.
    /// </summary>
    void PrepareMedia();

    /// <summary>
    /// Prepares the native libraries of the cameras and voices; called once on the UI thread before the first session.
    /// </summary>
    void PrepareConference();

    /// <summary>
    /// Asks the user for the devices that are not allowed yet.
    /// </summary>
    /// <param name="camera">Whether to ask for the camera.</param>
    /// <param name="microphone">Whether to ask for the microphone.</param>
    /// <returns>What is allowed now, the devices that were not asked for too.</returns>
    Task<CaptureAccess> RequestCaptureAccessAsync(bool camera, bool microphone);

    /// <summary>
    /// Describes the camera for the current position of the screen.
    /// </summary>
    /// <returns>The camera, or <see langword="null"/> when there is none.</returns>
    CaptureDevice? GetCamera();

    /// <summary>
    /// Chooses the camera on the other side of the device.
    /// </summary>
    void SwitchCamera();

    /// <summary>
    /// Tells the system that a session started or ended: it keeps the session alive off the screen, the screen on and
    /// the sound to itself.
    /// </summary>
    /// <param name="active">Whether a session runs.</param>
    void SetSessionActive(bool active);

    /// <summary>
    /// Reads the text in the clipboard of the system, whatever form it was copied in (plain text, a link, a page).
    /// </summary>
    /// <returns>The text, or <see langword="null"/> when the clipboard holds none.</returns>
    string? ReadClipboardText();

    /// <summary>
    /// Gets the player of the short sounds of the application, such as the one of a new message.
    /// </summary>
    ISoundPlayer Sounds { get; }

    /// <summary>
    /// Holds the screen on its side, whichever way the device is turned, or lets it go: then the screen stands upright
    /// and follows the device again once the device is held upright too.
    /// </summary>
    /// <param name="hold">Whether to hold the screen on its side.</param>
    void HoldLandscape(bool hold);
}
