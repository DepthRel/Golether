using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Golether.UI.Controls;

/// <summary>
/// The cursor shown over the video while the pen is picked up: a pen silhouette whose nib sits exactly where the
/// stroke starts.
/// </summary>
public static class PenCursor
{
    /// <summary>
    /// The size of the picture in pixels.
    /// </summary>
    private const int Size = 32;

    /// <summary>
    /// The silhouette: the nib, the body and the cap, drawn in a 32×32 box with the nib at the bottom left.
    /// </summary>
    private const string Silhouette =
        "M 2,30 L 6.5,21.5 L 10.5,25.5 Z " +
        "M 5.5,22.5 L 22,6 L 26,10 L 9.5,26.5 Z " +
        "M 22.5,5.5 L 25.5,2.5 L 29.5,6.5 L 26.5,9.5 Z";

    /// <summary>
    /// The cursor, built once.
    /// </summary>
    private static Cursor? _cursor;

    /// <summary>
    /// Returns the pen cursor, or the standard cross when the picture cannot be built.
    /// </summary>
    /// <returns>The cursor.</returns>
    public static Cursor Get()
    {
        if (_cursor is not null)
        {
            return _cursor;
        }

        try
        {
            _cursor = Build();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or PlatformNotSupportedException)
        {
            _cursor = new Cursor(StandardCursorType.Cross);
        }

        return _cursor;
    }

    /// <summary>
    /// Draws the silhouette into a picture and makes a cursor of it.
    /// </summary>
    /// <returns>The cursor.</returns>
    private static Cursor Build()
    {
        var geometry = Geometry.Parse(Silhouette);
        var bitmap = new RenderTargetBitmap(new PixelSize(Size, Size), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            // White around a dark body, so the pen is visible over a picture of any colour.
            context.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x24)), new Pen(Brushes.White, 2.4), geometry);
        }

        return new Cursor(bitmap, new PixelPoint(2, 30));
    }
}
