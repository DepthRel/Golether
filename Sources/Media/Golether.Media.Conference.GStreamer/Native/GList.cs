using System.Runtime.InteropServices;

namespace Golether.Media.Conference.GStreamer.Native;

/// <summary>
/// <c>GList</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GList
{
    /// <summary>
    /// The element.
    /// </summary>
    public nint Data;

    /// <summary>
    /// The next node.
    /// </summary>
    public nint Next;

    /// <summary>
    /// The previous node.
    /// </summary>
    public nint Previous;
}
