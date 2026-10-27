namespace Golether.Session;

/// <summary>
/// Makes the small pictures of the folder strip on the host. Decoding pictures belongs to the platform, so the
/// session takes it from outside.
/// </summary>
public interface IThumbnailRenderer
{
    /// <summary>
    /// Makes a small JPEG of a picture, upright and no larger than the protocol allows.
    /// </summary>
    /// <param name="picture">The bytes of the picture as participants receive them, or a frame of a video.</param>
    /// <returns>The JPEG bytes, or <see langword="null"/> when the picture cannot be decoded.</returns>
    byte[]? Render(Stream picture);
}
