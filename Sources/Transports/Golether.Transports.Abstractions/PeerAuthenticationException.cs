namespace Golether.Transports;

/// <summary>
/// The peer did not prove ownership of the expected key.
/// </summary>
public sealed class PeerAuthenticationException : IOException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PeerAuthenticationException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public PeerAuthenticationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
