namespace Golether.Session;

/// <summary>
/// Joining a session failed.
/// </summary>
public sealed class SessionJoinException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SessionJoinException"/> class.
    /// </summary>
    /// <param name="message">The message for the user.</param>
    /// <param name="innerException">The cause.</param>
    public SessionJoinException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
