using System.Runtime.InteropServices;

namespace Golether.Media.Player.Mpv;

/// <summary>
/// <c>mpv_event_end_file</c> (leading fields).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventEndFile
{
    /// <summary>
    /// The reason (<c>mpv_end_file_reason</c>): 0 EOF, 2 stop, 3 quit, 4 error, 5 redirect.
    /// </summary>
    public int Reason;

    /// <summary>
    /// The error code when <see cref="Reason"/> is 4.
    /// </summary>
    public int Error;
}
