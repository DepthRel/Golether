using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Golether.UI.Controls;

/// <summary>
/// Draws parts of a timeline track.
/// </summary>
public sealed class RangeBar : Control
{
    /// <summary>
    /// The drawn parts.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<FractionRange>?> RangesProperty =
        AvaloniaProperty.Register<RangeBar, IReadOnlyList<FractionRange>?>(nameof(Ranges));

    /// <summary>
    /// The color of the parts.
    /// </summary>
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<RangeBar, IBrush?>(nameof(Fill), new SolidColorBrush(Color.FromArgb(0x66, 0xDD, 0xE7, 0xE5)));

    /// <summary>
    /// The color of the rest of the track.
    /// </summary>
    public static readonly StyledProperty<IBrush?> TrackFillProperty =
        AvaloniaProperty.Register<RangeBar, IBrush?>(nameof(TrackFill), new SolidColorBrush(Color.FromRgb(0x22, 0x30, 0x36)));

    /// <summary>
    /// Initializes static members of the <see cref="RangeBar"/> class.
    /// </summary>
    static RangeBar()
    {
        AffectsRender<RangeBar>(RangesProperty, FillProperty, TrackFillProperty);
    }

    /// <summary>
    /// Gets or sets the drawn parts.
    /// </summary>
    public IReadOnlyList<FractionRange>? Ranges
    {
        get => GetValue(RangesProperty);
        set => SetValue(RangesProperty, value);
    }

    /// <summary>
    /// Gets or sets the color of the parts.
    /// </summary>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>
    /// Gets or sets the color of the rest of the track.
    /// </summary>
    public IBrush? TrackFill
    {
        get => GetValue(TrackFillProperty);
        set => SetValue(TrackFillProperty, value);
    }

    /// <summary>
    /// Gets or sets the track whose thumb defines where 0 and 1 are.
    /// </summary>
    internal Track? Track { get; set; }

    /// <summary>
    /// Returns the horizontal extent of a part.
    /// </summary>
    /// <param name="range">The part.</param>
    /// <param name="width">The width of the bar.</param>
    /// <param name="inset">The distance of 0 and 1 from the edges (half the thumb).</param>
    /// <returns>The left edge and the width.</returns>
    public static (double X, double Width) Place(FractionRange range, double width, double inset)
    {
        var usable = Math.Max(0, width - (2 * inset));
        var start = Math.Clamp(range.Start, 0, 1);
        var end = Math.Clamp(range.End, start, 1);
        var left = inset + (start * usable);
        var right = inset + (end * usable);

        // The ends of the track are covered as well when a part touches them.
        if (start <= 0)
        {
            left = 0;
        }

        if (end >= 1)
        {
            right = width;
        }

        return (left, Math.Max(0, right - left));
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var inset = (Track?.Thumb?.Bounds.Width ?? 0) / 2;
        var radius = Bounds.Height / 2;
        if (TrackFill is { } track)
        {
            context.DrawRectangle(track, null, new RoundedRect(new Rect(0, 0, Bounds.Width, Bounds.Height), radius));
        }

        if (Ranges is not { Count: > 0 } ranges || Fill is not { } fill)
        {
            return;
        }

        foreach (var range in ranges)
        {
            var (x, width) = Place(range, Bounds.Width, inset);
            if (width > 0.5)
            {
                context.DrawRectangle(fill, null, new RoundedRect(new Rect(x, 0, width, Bounds.Height), radius));
            }
        }
    }
}
