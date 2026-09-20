using System.Runtime.InteropServices;

namespace Golether.Media.Conference.GStreamer.Native;

/// <summary>
/// <c>GValue</c>: a type tag and two 64-bit data slots. Must be zeroed before <c>g_value_init</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GValue
{
    /// <summary>
    /// The type of the value.
    /// </summary>
    public nuint GType;

    /// <summary>
    /// The first data slot.
    /// </summary>
    public ulong Data0;

    /// <summary>
    /// The second data slot.
    /// </summary>
    public ulong Data1;
}
