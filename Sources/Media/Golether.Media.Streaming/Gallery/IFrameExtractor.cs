namespace Golether.Media.Streaming.Gallery;

/// <summary>
/// Decodes one frame of a file the pictures of a folder cannot show as they are (a HEIC or TIFF photograph, a video)
/// and writes it as a JPEG.
/// </summary>
public interface IFrameExtractor
{
    /// <summary>
    /// Gets a value indicating whether frames can be decoded on this device at all.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Decodes a frame and writes it as a JPEG without metadata.
    /// </summary>
    /// <param name="source">The file.</param>
    /// <param name="target">The JPEG to write; replaced when it exists.</param>
    /// <param name="position">Where the frame is taken, as a part of the duration from 0 to 1; 0 for a picture.</param>
    /// <param name="maxSide">The longest side of the JPEG, or 0 to keep the size of the frame.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the JPEG was written.</returns>
    Task<bool> ExtractAsync(string source, string target, double position, int maxSide, CancellationToken cancellationToken);
}
