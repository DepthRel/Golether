using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Golether.UI.Controls;

/// <summary>
/// A part of the timeline as fractions of the duration.
/// </summary>
/// <param name="Start">The start, 0–1.</param>
/// <param name="End">The end, 0–1.</param>
public readonly record struct FractionRange(double Start, double End);

/// <summary>
/// The timeline slider: the standard slider with the parts that can be played without waiting drawn on its track.
/// </summary>
public sealed class TimelineSlider : Slider
{
    /// <summary>
    /// The parts that can be played without waiting.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<FractionRange>?> BufferedRangesProperty =
        AvaloniaProperty.Register<TimelineSlider, IReadOnlyList<FractionRange>?>(nameof(BufferedRanges));

    /// <summary>
    /// The height of the track line, the same as the Fluent slider uses.
    /// </summary>
    private const double TrackHeight = 4;

    /// <summary>
    /// The bar under the track: the parts that can be played without waiting.
    /// </summary>
    private readonly RangeBar _bar = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Height = TrackHeight };

    /// <summary>
    /// Gets or sets the parts that can be played without waiting.
    /// </summary>
    public IReadOnlyList<FractionRange>? BufferedRanges
    {
        get => GetValue(BufferedRangesProperty);
        set => SetValue(BufferedRangesProperty, value);
    }

    /// <summary>
    /// Gets the bar with the buffered parts (tests).
    /// </summary>
    internal RangeBar Bar => _bar;

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(Slider);

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        (_bar.Parent as Panel)?.Children.Remove(_bar);
        var track = e.NameScope.Find<Track>("PART_Track");
        _bar.Track = track;
        if (track?.Parent is not Panel panel)
        {
            return;
        }

        // The bar lies under the track: the played part (the filled button) and the thumb stay above it, while the
        // rest of the track is transparent in the timeline style, so the buffered parts show through.
        Grid.SetRow(_bar, Grid.GetRow(track));
        Grid.SetColumn(_bar, Grid.GetColumn(track));
        Grid.SetColumnSpan(_bar, Math.Max(1, Grid.GetColumnSpan(track)));
        _bar.Margin = track.Margin;
        panel.Children.Insert(panel.Children.IndexOf(track), _bar);
        _bar.Ranges = BufferedRanges;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BufferedRangesProperty)
        {
            _bar.Ranges = BufferedRanges;
        }
    }
}

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
