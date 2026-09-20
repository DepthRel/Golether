using System.Runtime.InteropServices;

namespace Golether.Media.Conference.GStreamer.Native;

/// <summary>
/// <c>GError</c> (leading fields).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GError
{
    /// <summary>
    /// The error domain.
    /// </summary>
    public uint Domain;

    /// <summary>
    /// The error code.
    /// </summary>
    public int Code;

    /// <summary>
    /// The UTF-8 message.
    /// </summary>
    public nint Message;
}
