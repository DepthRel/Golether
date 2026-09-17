namespace Golether.Media.Streaming.Caching;

/// <summary>
/// Publishes streams under URIs the player can open.
/// </summary>
public interface IMediaUriRegistry
{
    /// <summary>
    /// Registers a stream factory.
    /// </summary>
    /// <param name="factory">Creates a new readable, seekable stream for each open.</param>
    /// <returns>The URI to pass to the player.</returns>
    Uri Register(Func<Stream> factory);

    /// <summary>
    /// Removes a registration.
    /// </summary>
    /// <param name="uri">The URI returned by <see cref="Register"/>.</param>
    void Unregister(Uri uri);
}
