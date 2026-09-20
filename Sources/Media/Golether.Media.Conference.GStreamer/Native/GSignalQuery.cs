using System.Runtime.InteropServices;

namespace Golether.Media.Conference.GStreamer.Native;

/// <summary>
/// <c>GSignalQuery</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GSignalQuery
{
    /// <summary>
    /// The signal identifier.
    /// </summary>
    public uint SignalId;

    /// <summary>
    /// The signal name.
    /// </summary>
    public nint SignalName;

    /// <summary>
    /// The owner type.
    /// </summary>
    public nuint InstanceType;

    /// <summary>
    /// The signal flags.
    /// </summary>
    public int SignalFlags;

    /// <summary>
    /// The return type.
    /// </summary>
    public nuint ReturnType;

    /// <summary>
    /// The number of parameters.
    /// </summary>
    public uint ParameterCount;

    /// <summary>
    /// The parameter types.
    /// </summary>
    public nint ParameterTypes;
}
