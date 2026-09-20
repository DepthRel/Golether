namespace Golether.Media.Player.Mpv;

/// <summary>
/// A libmpv call failed.
/// </summary>
public sealed class MpvException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MpvException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public MpvException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
