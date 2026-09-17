namespace Golether.Media.Player;

/// <summary>
/// A mouse action on the video picture.
/// </summary>
public enum VideoPointerAction
{
    /// <summary>
    /// A left click.
    /// </summary>
    Click = 0,

    /// <summary>
    /// A left double click.
    /// </summary>
    DoubleClick = 1,
}

/// <summary>
/// Settings and input of a player that concern only this device: they are never synchronized.
/// </summary>
public interface ILocalPlayerControls
{
    /// <summary>
    /// Raised when the user clicks the video picture. May be raised on any thread.
    /// </summary>
    event EventHandler<VideoPointerAction>? VideoPointer;

    /// <summary>
    /// Sets the playback volume of this device.
    /// </summary>
    /// <param name="percent">The volume, 0–100.</param>
    void SetVolume(double percent);

    /// <summary>
    /// Mutes or unmutes the playback on this device.
    /// </summary>
    /// <param name="muted">Whether the sound is off.</param>
    void SetMuted(bool muted);
}
