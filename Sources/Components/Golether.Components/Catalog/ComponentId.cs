namespace Golether.Components.Catalog;

/// <summary>
/// A native component of the application.
/// </summary>
public enum ComponentId
{
    /// <summary>
    /// libmpv: decoding and showing the video.
    /// </summary>
    Video = 0,

    /// <summary>
    /// GStreamer: cameras, voice and echo cancellation.
    /// </summary>
    Conference = 1,

    /// <summary>
    /// AmneziaWG: the tunnel Golether raises by itself, so a session network needs nothing installed beforehand.
    /// </summary>
    Tunnel = 2,
}
