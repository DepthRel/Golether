namespace Golether.Media.Player;

/// <summary>
/// A player whose subtitles can be enlarged: they follow the height of the picture, and on an upright phone the picture is
/// low, so the text would be too small to read.
/// </summary>
public interface ISubtitleScale
{
    /// <summary>
    /// Sets the size of the subtitles on this device.
    /// </summary>
    /// <param name="scale">The size, 1 for the normal one.</param>
    void SetSubtitleScale(double scale);
}
