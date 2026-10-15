using System.Runtime.InteropServices;
using Golether.Media.Player.Enums;

namespace Golether.Media.Player.Mpv;

/// <summary>
/// <c>mpv_event_property</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventProperty
{
    /// <summary>
    /// The property name.
    /// </summary>
    public nint Name;

    /// <summary>
    /// The value format; <see cref="MpvFormat.None"/> when the property is unavailable.
    /// </summary>
    public MpvFormat Format;

    /// <summary>
    /// A pointer to the value.
    /// </summary>
    public nint Data;
}
