using Avalonia;

namespace Golether.UI.Controls;

/// <summary>
/// Where a picture lies in an area: fitted whole, or enlarged around a point of it. The picture of a shown folder and
/// the strokes over it are placed by the same rule, so a stroke stays on the spot it was drawn on at any zoom.
/// </summary>
public static class PictureFrame
{
    /// <summary>
    /// Returns the part of an area a picture takes when it is fitted whole, keeping its ratio.
    /// </summary>
    /// <param name="size">The size of the area.</param>
    /// <param name="aspect">The width of the picture divided by its height, or <c>0</c> to use the whole area.</param>
    /// <returns>The picture rectangle.</returns>
    public static Rect Fit(Size size, double aspect)
    {
        if (aspect <= 0 || size.Width <= 0 || size.Height <= 0)
        {
            return new Rect(size);
        }

        var own = size.Width / size.Height;
        if (Math.Abs(own - aspect) < 0.0001)
        {
            return new Rect(size);
        }

        if (own > aspect)
        {
            var width = size.Height * aspect;
            return new Rect((size.Width - width) / 2, 0, width, size.Height);
        }

        var height = size.Width / aspect;
        return new Rect(0, (size.Height - height) / 2, size.Width, height);
    }

    /// <summary>
    /// Returns where a picture lies when it is enlarged: the given point of it is in the middle of the area as far as
    /// the picture can be moved without leaving an empty edge; a side shorter than the area stays centred.
    /// </summary>
    /// <param name="size">The size of the area.</param>
    /// <param name="aspect">The width of the picture divided by its height, or <c>0</c> to use the whole area.</param>
    /// <param name="zoom">How many times the fitted picture is enlarged; 1 or less fits it whole.</param>
    /// <param name="center">The point of the picture in the middle, as shares of its width and height.</param>
    /// <returns>The picture rectangle; it may reach beyond the area.</returns>
    public static Rect Place(Size size, double aspect, double zoom, Point center)
    {
        var fitted = Fit(size, aspect);
        if (zoom <= 1 || fitted.Width <= 0 || fitted.Height <= 0)
        {
            return fitted;
        }

        var width = fitted.Width * zoom;
        var height = fitted.Height * zoom;
        return new Rect(Axis(size.Width, width, center.X), Axis(size.Height, height, center.Y), width, height);
    }

    /// <summary>
    /// Places the picture along one side of the area.
    /// </summary>
    /// <param name="room">The length of the area.</param>
    /// <param name="length">The length of the picture.</param>
    /// <param name="center">The share of the picture in the middle.</param>
    /// <returns>The start of the picture.</returns>
    private static double Axis(double room, double length, double center)
        => length <= room ? (room - length) / 2 : Math.Clamp((room / 2) - (center * length), room - length, 0);
}
