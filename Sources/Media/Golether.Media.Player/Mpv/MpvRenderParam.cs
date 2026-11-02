using System.Runtime.InteropServices;
using Golether.Media.Player.Enums;

namespace Golether.Media.Player.Mpv;

/// <summary>
/// One parameter of the render API (<c>mpv_render_param</c>).
/// </summary>
/// <param name="Type">The kind.</param>
/// <param name="Data">The value, its layout depends on the kind.</param>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct MpvRenderParam(MpvRenderParamType Type, nint Data);
