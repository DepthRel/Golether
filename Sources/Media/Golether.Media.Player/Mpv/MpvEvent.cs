using System.Runtime.InteropServices;

namespace Golether.Media.Player.Mpv;

/// <summary>
/// <c>mpv_event</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MpvEvent
{
    /// <summary>
    /// The event identifier.
    /// </summary>
    public MpvEventId EventId;

    /// <summary>
    /// The error code (for replies).
    /// </summary>
    public int Error;

    /// <summary>
    /// The user data of the request or observed property.
    /// </summary>
    public ulong ReplyUserData;

    /// <summary>
    /// The event-specific data.
    /// </summary>
    public nint Data;
}
