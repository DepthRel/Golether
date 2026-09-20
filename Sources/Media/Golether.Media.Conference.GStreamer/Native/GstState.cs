namespace Golether.Media.Conference.GStreamer.Native;

/// <summary>
/// <c>GstState</c>.
/// </summary>
internal enum GstState
{
    /// <summary>
    /// No pending state.
    /// </summary>
    VoidPending = 0,

    /// <summary>
    /// Resources released.
    /// </summary>
    Null = 1,

    /// <summary>
    /// Resources allocated.
    /// </summary>
    Ready = 2,

    /// <summary>
    /// Prerolled.
    /// </summary>
    Paused = 3,

    /// <summary>
    /// Running.
    /// </summary>
    Playing = 4,
}
