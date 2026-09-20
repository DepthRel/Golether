using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace Golether.UI.Controls;

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
