using Golether.Core.Media;

namespace Golether.Media.Streaming.Protocol;

/// <summary>
/// Provides chunks of a media file.
/// </summary>
public interface IChunkSource : IAsyncDisposable
{
    /// <summary>
    /// Gets the media descriptor.
    /// </summary>
    MediaDescriptor Media { get; }

    /// <summary>
    /// Returns a chunk.
    /// </summary>
    /// <param name="index">The chunk index.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The verified chunk data.</returns>
    /// <exception cref="ChunkUnavailableException">The chunk cannot be obtained.</exception>
    ValueTask<byte[]> GetChunkAsync(long index, CancellationToken cancellationToken);
}
