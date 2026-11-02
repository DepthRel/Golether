using System.Runtime.InteropServices;

namespace Golether.Media.Player.Mpv;

/// <summary>
/// The framebuffer mpv draws into (<c>mpv_opengl_fbo</c>).
/// </summary>
/// <param name="Framebuffer">The OpenGL framebuffer.</param>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="InternalFormat">The format of the framebuffer, 0 when unknown.</param>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct MpvOpenGlFbo(int Framebuffer, int Width, int Height, int InternalFormat);
