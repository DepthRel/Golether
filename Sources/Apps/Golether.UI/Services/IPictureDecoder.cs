using Avalonia.Media.Imaging;

namespace Golether.UI.Services;

/// <summary>
/// Turns the bytes of a shown picture into something the screen can draw.
/// </summary>
public interface IPictureDecoder
{
    /// <summary>
    /// Decodes a picture upright and no larger than needed: a photograph of fifty megapixels is not kept in memory
    /// at its full size to be shown on a screen of two.
    /// </summary>
    /// <param name="picture">The bytes; they came over the network and are not trusted.</param>
    /// <param name="maxSide">The longest side of the result, in pixels.</param>
    /// <returns>The picture, or <see langword="null"/> when the bytes are not a picture that can be shown.</returns>
    Bitmap? Decode(byte[] picture, int maxSide);
}
