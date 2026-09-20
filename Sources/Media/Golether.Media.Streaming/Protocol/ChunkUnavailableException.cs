namespace Golether.Media.Streaming.Protocol;

/// <summary>
/// A chunk cannot be obtained from the source.
/// </summary>
public sealed class ChunkUnavailableException : IOException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChunkUnavailableException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public ChunkUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
