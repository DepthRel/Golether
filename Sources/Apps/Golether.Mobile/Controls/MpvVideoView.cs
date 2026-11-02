using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using Golether.Media.Player.Mpv;

namespace Golether.Mobile.Controls;

/// <summary>
/// The video of a player as an ordinary element of the interface: mpv draws into the OpenGL surface of the view, so the
/// controls, reactions and strokes of the session are drawn over the picture without separate windows.
/// </summary>
public sealed class MpvVideoView : OpenGlControlBase
{
    /// <summary>
    /// The drawing context of the current OpenGL surface.
    /// </summary>
    private MpvRenderContext? _context;

    /// <summary>
    /// Gets or sets the player to show; it may come after the view appeared.
    /// </summary>
    public MpvPlayer? Player
    {
        get;
        set
        {
            field = value;
            RequestNextFrameRendering();
        }
    }

    /// <inheritdoc />
    protected override void OnOpenGlInit(GlInterface gl) => ReleaseContext();

    /// <inheritdoc />
    protected override void OnOpenGlDeinit(GlInterface gl) => ReleaseContext();

    /// <inheritdoc />
    protected override void OnOpenGlLost() => ReleaseContext();

    /// <inheritdoc />
    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_context is null && Player is { } player)
        {
            _context = player.CreateRenderContext(gl.GetProcAddress);
            _context.FrameReady += (_, _) => Dispatcher.UIThread.Post(RequestNextFrameRendering);
        }

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

        _context?.Render(fb, (int)(Bounds.Width * scaling), (int)(Bounds.Height * scaling));
    }

    /// <summary>
    /// Frees the drawing context of a surface that went away; mpv refuses a second context while the first one lives.
    /// </summary>
    private void ReleaseContext()
    {
        _context?.Dispose();
        _context = null;
    }
}
