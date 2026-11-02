using System.Runtime.InteropServices;

namespace Golether.Media.Player.Mpv;

/// <summary>
/// How mpv reaches the OpenGL functions (<c>mpv_opengl_init_params</c>).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MpvOpenGlInitParams
{
    /// <summary>
    /// Returns the address of an OpenGL function by name.
    /// </summary>
    public delegate* unmanaged[Cdecl]<nint, byte*, nint> GetProcAddress;

    /// <summary>
    /// The first argument of <see cref="GetProcAddress"/>.
    /// </summary>
    public nint Context;
}
