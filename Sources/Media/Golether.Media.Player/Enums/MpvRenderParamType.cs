namespace Golether.Media.Player.Enums;

/// <summary>
/// The kinds of parameters of the render API of libmpv (<c>mpv_render_param_type</c>).
/// </summary>
internal enum MpvRenderParamType
{
    /// <summary>
    /// The end of a parameter list.
    /// </summary>
    Invalid = 0,

    /// <summary>
    /// The graphics API, a string.
    /// </summary>
    ApiType = 1,

    /// <summary>
    /// How to reach OpenGL, <c>mpv_opengl_init_params</c>.
    /// </summary>
    OpenGlInitParams = 2,

    /// <summary>
    /// The framebuffer to draw into, <c>mpv_opengl_fbo</c>.
    /// </summary>
    OpenGlFbo = 3,

    /// <summary>
    /// Whether to draw upside down, an integer.
    /// </summary>
    FlipY = 4,
}
