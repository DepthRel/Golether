namespace Golether.Client.Enums;

/// <summary>
/// A feature of the client that is bought once ("Pro"); the desktop application has them all.
/// </summary>
public enum PaidFeature
{
    /// <summary>
    /// Drawing over the video with the pen; the strokes of the others are seen without it.
    /// </summary>
    Drawing = 0,

    /// <summary>
    /// Subtitles: the embedded tracks and files of the user; the sound tracks are free.
    /// </summary>
    Subtitles = 1,
}
