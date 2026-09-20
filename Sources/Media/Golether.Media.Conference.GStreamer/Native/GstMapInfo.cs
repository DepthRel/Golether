using System.Runtime.InteropServices;

namespace Golether.Media.Conference.GStreamer.Native;

/// <summary>
/// <c>GstMapInfo</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GstMapInfo
{
    /// <summary>
    /// The mapped memory object.
    /// </summary>
    public nint Memory;

    /// <summary>
    /// The map flags.
    /// </summary>
    public int Flags;

    /// <summary>
    /// A pointer to the data.
    /// </summary>
    public nint Data;

    /// <summary>
    /// The valid size.
    /// </summary>
    public nuint Size;

    /// <summary>
    /// The maximum size.
    /// </summary>
    public nuint MaxSize;

    /// <summary>
    /// User data (4 pointers).
    /// </summary>
    public nint UserData0;

    /// <summary>
    /// User data.
    /// </summary>
    public nint UserData1;

    /// <summary>
    /// User data.
    /// </summary>
    public nint UserData2;

    /// <summary>
    /// User data.
    /// </summary>
    public nint UserData3;

    /// <summary>
    /// Reserved (4 pointers).
    /// </summary>
    public nint Reserved0;

    /// <summary>
    /// Reserved.
    /// </summary>
    public nint Reserved1;

    /// <summary>
    /// Reserved.
    /// </summary>
    public nint Reserved2;

    /// <summary>
    /// Reserved.
    /// </summary>
    public nint Reserved3;
}
