using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using Golether.Session;
using Golether.Sync.Protocol;

namespace Golether.UI.Services;

/// <summary>
/// Decodes shown pictures with Skia, the graphics library the interface already draws with: small pictures for the
/// folder strip on the host, and pictures for the screen everywhere. The orientation the camera wrote is applied, so
/// a photo taken upright is shown upright.
/// </summary>
public sealed class SkiaPictureCodec : IThumbnailRenderer, IPictureDecoder
{
    /// <summary>
    /// The most pixels a picture is decoded at: the size of a decoded picture is four bytes a pixel, and a small file
    /// may declare a huge picture.
    /// </summary>
    private const long MAX_DECODED_PIXELS = 100L * 1000 * 1000;

    /// <summary>
    /// The qualities a small picture is tried at, from the best: the first one that fits the protocol wins.
    /// </summary>
    private static readonly int[] _qualities = [80, 65, 50, 35];

    /// <inheritdoc />
    public byte[]? Render(Stream picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        using var data = SKData.Create(picture);
        using var bitmap = data is null ? null : DecodeUpright(data, ThumbnailMessage.MAX_DIMENSION);
        if (bitmap is null)
        {
            return null;
        }

        using var image = SKImage.FromBitmap(bitmap);
        foreach (var quality in _qualities)
        {
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, quality);
            if (encoded is not null && encoded.Size <= ThumbnailMessage.MAX_IMAGE_BYTES)
            {
                return encoded.ToArray();
            }
        }

        return null;
    }

    /// <inheritdoc />
    public Bitmap? Decode(byte[] picture, int maxSide)
    {
        ArgumentNullException.ThrowIfNull(picture);
        using var data = SKData.CreateCopy(picture);
        using var bitmap = DecodeUpright(data, Math.Max(16, maxSide));
        if (bitmap is null)
        {
            return null;
        }

        return new Bitmap(
            PixelFormat.Bgra8888,
            AlphaFormat.Premul,
            bitmap.GetPixels(),
            new PixelSize(bitmap.Width, bitmap.Height),
            new Vector(96, 96),
            bitmap.RowBytes);
    }

    /// <summary>
    /// Decodes a picture to fit a square of the given side and turns it upright.
    /// </summary>
    /// <param name="data">The bytes.</param>
    /// <param name="maxSide">The longest side of the result.</param>
    /// <returns>The picture, or <see langword="null"/> when it cannot be decoded.</returns>
    private static SKBitmap? DecodeUpright(SKData data, int maxSide)
    {
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
        {
            return null;
        }

        // JPEG decodes at an eighth, a quarter or a half of its size directly; other formats decode whole first.
        var scale = Math.Min(1f, (float)maxSide / Math.Max(codec.Info.Width, codec.Info.Height));
        var size = codec.GetScaledDimensions(scale);
        if ((long)size.Width * size.Height > MAX_DECODED_PIXELS)
        {
            return null;
        }

        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var decoded = new SKBitmap(info);
        var result = codec.GetPixels(info, decoded.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            return null;
        }

        var fit = Math.Min(1.0, (double)maxSide / Math.Max(size.Width, size.Height));
        var target = new SKImageInfo(Math.Max(1, (int)(size.Width * fit)), Math.Max(1, (int)(size.Height * fit)), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var resized = target.Width == size.Width && target.Height == size.Height
            ? decoded.Copy()
            : decoded.Resize(target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return resized is null ? null : Orient(resized, codec.EncodedOrigin);
    }

    /// <summary>
    /// Turns a decoded picture the way its EXIF orientation says.
    /// </summary>
    /// <param name="source">The picture as it is stored.</param>
    /// <param name="origin">The orientation.</param>
    /// <returns>A new, upright picture.</returns>
    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = swap ? source.Height : source.Width;
        var height = swap ? source.Width : source.Height;

        // Where a pixel (x, y) of the stored picture goes: x' = a·x + b·y + c, y' = d·x + e·y + f.
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, width, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, width, 0, -1, height, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, height, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, width, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, width, -1, 0, height, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, height, 0, 0, 1),
            _ => SKMatrix.Identity,
        };

        var upright = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(upright))
        {
            canvas.SetMatrix(matrix);
            canvas.DrawBitmap(source, 0, 0);
        }

        return upright;
    }
}
