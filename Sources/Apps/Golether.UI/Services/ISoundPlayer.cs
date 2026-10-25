namespace Golether.UI.Services;

/// <summary>
/// Plays the short sounds of the application through the sound system of the device.
/// </summary>
public interface ISoundPlayer
{
    /// <summary>
    /// Starts a sound and returns at once. A sound that cannot be played is skipped without an error: a notification
    /// is never worth breaking anything for.
    /// </summary>
    /// <param name="wave">A complete WAV file; it is kept and must not be changed afterwards.</param>
    void Play(byte[] wave);
}
