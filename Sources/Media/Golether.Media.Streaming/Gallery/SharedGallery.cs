using Golether.Core.Media;
using Golether.Media.Streaming.Protocol;

namespace Golether.Media.Streaming.Gallery;

/// <summary>
/// The pictures the host shows at the moment, as the chunk server sees them: the showing may change or end while
/// participants are connected.
/// </summary>
public sealed class SharedGallery : ISharedFileSet
{
    /// <summary>
    /// The current showing, or <see langword="null"/>.
    /// </summary>
    private GalleryCatalog? _current;

    /// <summary>
    /// Gets the current showing, or <see langword="null"/>.
    /// </summary>
    public GalleryCatalog? Current => Volatile.Read(ref _current);

    /// <summary>
    /// Replaces the showing.
    /// </summary>
    /// <param name="catalog">The new showing, or <see langword="null"/> to end it.</param>
    /// <returns>The showing it replaced, or <see langword="null"/>; the caller ends it.</returns>
    public GalleryCatalog? Share(GalleryCatalog? catalog) => Interlocked.Exchange(ref _current, catalog);

    /// <inheritdoc />
    public bool TryFind(ulong key, out MediaDescriptor descriptor, out Func<Stream> open)
    {
        if (Current is { } catalog)
        {
            return catalog.TryFind(key, out descriptor, out open);
        }

        descriptor = null!;
        open = null!;
        return false;
    }
}
