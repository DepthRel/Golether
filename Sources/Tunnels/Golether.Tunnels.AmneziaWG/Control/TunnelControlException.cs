namespace Golether.Tunnels.AmneziaWG.Control;

/// <summary>
/// A tunnel operation failed.
/// </summary>
public sealed class TunnelControlException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TunnelControlException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public TunnelControlException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
