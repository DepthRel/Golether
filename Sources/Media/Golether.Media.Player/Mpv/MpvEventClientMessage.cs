using System.Runtime.InteropServices;

namespace Golether.Media.Player.Mpv;

/// <summary>
/// <c>mpv_event_client_message</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MpvEventClientMessage
{
    /// <summary>
    /// The number of arguments.
    /// </summary>
    public int NumArgs;

    /// <summary>
    /// The UTF-8 arguments.
    /// </summary>
    public byte** Args;
}
