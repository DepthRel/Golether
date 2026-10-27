namespace Golether.Media.Streaming.Gallery;

/// <summary>
/// How the files of a folder are prepared for a showing.
/// </summary>
public sealed record GalleryOptions
{
    /// <summary>
    /// Gets a value indicating whether the place, the camera and the comments are left out of the pictures.
    /// </summary>
    public bool StripMetadata { get; init; } = true;

    /// <summary>
    /// Gets the decoder of HEIC, AVIF and TIFF pictures and of video frames, or <see langword="null"/> when there is
    /// none: then such pictures are left out and videos have no small picture.
    /// </summary>
    public IFrameExtractor? Frames { get; init; }

    /// <summary>
    /// Gets the folder where a showing keeps the JPEG files it makes; each showing has a folder of its own inside,
    /// deleted when it ends.
    /// </summary>
    public required string WorkDirectory { get; init; }
}
