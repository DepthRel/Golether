namespace Golether.Media.Conference.GStreamer.Native;

/// <summary>
/// A GStreamer call failed.
/// </summary>
public sealed class GstException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GstException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public GstException(string message)
        : base(message)
    {
    }
}
