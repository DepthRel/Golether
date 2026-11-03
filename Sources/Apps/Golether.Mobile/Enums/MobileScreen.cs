namespace Golether.Mobile.Enums;

/// <summary>
/// The screens of the mobile client.
/// </summary>
public enum MobileScreen
{
    /// <summary>
    /// The application prepares its data.
    /// </summary>
    Loading = 0,

    /// <summary>
    /// The name and the invitation.
    /// </summary>
    Join = 1,

    /// <summary>
    /// Looking for a way to the host.
    /// </summary>
    Connecting = 2,

    /// <summary>
    /// The host decides; the verification code is shown.
    /// </summary>
    Approval = 3,

    /// <summary>
    /// The session: the video and its controls.
    /// </summary>
    Watch = 4,

    /// <summary>
    /// A new session this device hosts: its name and what it shows.
    /// </summary>
    Host = 5,
}
