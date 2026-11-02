using Avalonia;
using Avalonia.Media.Imaging;
using Golether.Sync.Protocol;

namespace Golether.Client.Services;

/// <summary>
/// Renders the round frame of the avatar chooser into the picture the others get. A photo in a circle of 256 pixels
/// can be larger than a message of the session takes, and the others would drop it without a word: the picture is
/// rendered smaller until it fits.
/// </summary>
public static class AvatarRendering
{
    /// <summary>
    /// The sides the picture is tried at, from the best.
    /// </summary>
    private static readonly int[] _sides = [256, 208, 160, 128];

    /// <summary>
    /// Renders the frame as PNG at the largest side that fits a message of the session.
    /// </summary>
    /// <param name="frame">The round frame, laid out on the screen.</param>
    /// <param name="frameSize">The side of the frame, in the units of the screen.</param>
    /// <returns>The picture, or <see langword="null"/> when even the smallest is too large.</returns>
    public static byte[]? Render(Visual frame, double frameSize)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameSize);

        foreach (var side in _sides)
        {
            var dpi = 96.0 * side / frameSize;
            using var target = new RenderTargetBitmap(new PixelSize(side, side), new Vector(dpi, dpi));
            target.Render(frame);

            using var stream = new MemoryStream();
            target.Save(stream, PngBitmapEncoderOptions.Default);
            if (stream.Length <= AvatarMessage.MAX_IMAGE_BYTES)
            {
                return stream.ToArray();
            }
        }

        return null;
    }
}
