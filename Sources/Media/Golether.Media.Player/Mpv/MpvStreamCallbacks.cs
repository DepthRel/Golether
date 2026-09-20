using System.Runtime.InteropServices;

namespace Golether.Media.Player.Mpv;

/// <summary>
/// <c>mpv_stream_cb_info</c>: callbacks of a custom stream.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MpvStreamCallbacks
{
    /// <summary>
    /// The opaque cookie passed to the callbacks.
    /// </summary>
    public nint Cookie;

    /// <summary>
    /// <c>int64_t read(void *cookie, char *buf, uint64_t nbytes)</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<nint, byte*, ulong, long> Read;

    /// <summary>
    /// <c>int64_t seek(void *cookie, int64_t offset)</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<nint, long, long> Seek;

    /// <summary>
    /// <c>int64_t size(void *cookie)</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<nint, long> Size;

    /// <summary>
    /// <c>void close(void *cookie)</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<nint, void> Close;

    /// <summary>
    /// <c>void cancel(void *cookie)</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<nint, void> Cancel;
}
