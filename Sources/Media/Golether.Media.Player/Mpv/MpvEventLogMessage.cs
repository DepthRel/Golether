using System.Runtime.InteropServices;

namespace Golether.Media.Player.Mpv;

/// <summary>
/// <c>mpv_event_log_message</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventLogMessage
{
    /// <summary>
    /// The module prefix.
    /// </summary>
    public nint Prefix;

    /// <summary>
    /// The level name.
    /// </summary>
    public nint Level;

    /// <summary>
    /// The message text.
    /// </summary>
    public nint Text;

    /// <summary>
    /// The numeric level.
    /// </summary>
    public int LogLevel;
}
