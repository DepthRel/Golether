using Golether.Core.Media;

namespace Golether.Media.Streaming.Protocol;

/// <summary>
/// The media file the host currently shares.
/// </summary>
public interface ISharedMedia
{
    /// <summary>
    /// Gets the descriptor, or <see langword="null"/> when nothing is shared.
    /// </summary>
    MediaDescriptor? Descriptor { get; }

    /// <summary>
    /// Opens the shared file for reading.
    /// </summary>
    /// <returns>A readable, seekable stream; the caller owns it.</returns>
    Stream OpenRead();
}
