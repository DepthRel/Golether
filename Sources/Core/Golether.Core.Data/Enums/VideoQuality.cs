namespace Golether.Core.Data.Enums;

/// <summary>
/// The quality of the camera stream sent to one participant.
/// </summary>
public enum VideoQuality
{
    /// <summary>
    /// 640×360, 15 frames per second.
    /// </summary>
    High = 0,

    /// <summary>
    /// 320×180, 10 frames per second, about a quarter of the bitrate: for weak connections.
    /// </summary>
    Low = 1,
}
