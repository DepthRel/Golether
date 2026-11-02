using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Golether.Media.Player.Enums;

namespace Golether.Media.Player.Mpv;

/// <summary>
/// Draws the video of a player into an OpenGL framebuffer of the application, so the picture is an ordinary part of the
/// interface and anything can be drawn over it. Every member but <see cref="FrameReady"/> needs the OpenGL context of
/// the view to be current.
/// </summary>
public sealed unsafe class MpvRenderContext : IDisposable
{
    /// <summary>
    /// The handle of the player.
    /// </summary>
    private readonly nint _player;

    /// <summary>
    /// Returns the addresses of OpenGL functions.
    /// </summary>
    private readonly Func<string, nint> _getProcAddress;

    /// <summary>
    /// Keeps this object alive for the callbacks of libmpv.
    /// </summary>
    private GCHandle _self;

    /// <summary>
    /// The native render context, zero once disposed.
    /// </summary>
    private nint _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="MpvRenderContext"/> class.
    /// </summary>
    /// <param name="player">The handle of the player.</param>
    /// <param name="getProcAddress">Returns the addresses of OpenGL functions.</param>
    internal MpvRenderContext(nint player, Func<string, nint> getProcAddress)
    {
        _getProcAddress = getProcAddress ?? throw new ArgumentNullException(nameof(getProcAddress));
        _player = player;
        _self = GCHandle.Alloc(this);

        var init = new MpvOpenGlInitParams { GetProcAddress = &ResolveProcAddress, Context = GCHandle.ToIntPtr(_self) };
        fixed (byte* api = "opengl\0"u8)
        {
            var parameters = stackalloc MpvRenderParam[]
            {
                new(MpvRenderParamType.ApiType, (nint)api),
                new(MpvRenderParamType.OpenGlInitParams, (nint)(&init)),
                new(MpvRenderParamType.Invalid, 0),
            };

            nint context;
            var error = LibMpv.RenderContextCreate(&context, player, parameters);
            if (error < 0)
            {
                _self.Free();
                LibMpv.Check(error, "creating the render context");
            }

            _context = context;
        }

        LibMpv.RenderContextSetUpdateCallback(_context, &OnUpdate, GCHandle.ToIntPtr(_self));

        // A file may already be open without a picture: the previous context went away, or there was none yet. Setting
        // the same value does nothing, so the track is taken off first and mpv chooses it again for this context.
        LibMpv.CommandAsync(_player, "set", "vid", "no");
        LibMpv.CommandAsync(_player, "set", "vid", "auto");
    }

    /// <summary>
    /// Raised on a thread of libmpv when there is a new frame to draw; the view asks for a render.
    /// </summary>
    public event EventHandler? FrameReady;

    /// <summary>
    /// Draws the current frame.
    /// </summary>
    /// <param name="framebuffer">The OpenGL framebuffer.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    public void Render(int framebuffer, int width, int height)
    {
        if (_context == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        var fbo = new MpvOpenGlFbo(framebuffer, width, height, 0);
        var flip = 1;
        var parameters = stackalloc MpvRenderParam[]
        {
            new(MpvRenderParamType.OpenGlFbo, (nint)(&fbo)),
            new(MpvRenderParamType.FlipY, (nint)(&flip)),
            new(MpvRenderParamType.Invalid, 0),
        };

        LibMpv.Check(LibMpv.RenderContextRender(_context, parameters), "rendering a frame");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_context == 0)
        {
            return;
        }

        // Freeing a context under running video is an error for mpv: it would drop the track, and a file without sound
        // with it. Taken off first, the video simply stops; the commands are queued in order, so this one comes first.
        LibMpv.CommandAsync(_player, "set", "vid", "no");

        LibMpv.RenderContextSetUpdateCallback(_context, null, 0);
        LibMpv.RenderContextFree(_context);
        _context = 0;
        _self.Free();
    }

    /// <summary>
    /// Answers libmpv with the address of an OpenGL function.
    /// </summary>
    /// <param name="self">The handle of this object.</param>
    /// <param name="name">The name of the function.</param>
    /// <returns>The address, zero when there is no such function.</returns>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint ResolveProcAddress(nint self, byte* name)
    {
        var context = (MpvRenderContext)GCHandle.FromIntPtr(self).Target!;

        return context._getProcAddress(Marshal.PtrToStringUTF8((nint)name) ?? string.Empty);
    }

    /// <summary>
    /// Tells the view that there is a new frame.
    /// </summary>
    /// <param name="self">The handle of this object.</param>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnUpdate(nint self)
    {
        var context = (MpvRenderContext)GCHandle.FromIntPtr(self).Target!;
        context.FrameReady?.Invoke(context, EventArgs.Empty);
    }
}
