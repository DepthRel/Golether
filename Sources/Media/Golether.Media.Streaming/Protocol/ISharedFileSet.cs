using Golether.Core.Media;

namespace Golether.Media.Streaming.Protocol;

/// <summary>
/// Files shared next to the main media (the pictures of a shown folder), found by the key a request names.
/// </summary>
public interface ISharedFileSet
{
    /// <summary>
    /// Looks a shared file up by its media key.
    /// </summary>
    /// <param name="key">The media key from a request (<see cref="ChunkProtocol.GetMediaKey"/>).</param>
    /// <param name="descriptor">The descriptor of the file.</param>
    /// <param name="open">Opens a readable, seekable stream of exactly the bytes the descriptor describes.</param>
    /// <returns><see langword="true"/> when the key belongs to a shared file.</returns>
    bool TryFind(ulong key, out MediaDescriptor descriptor, out Func<Stream> open);
}
