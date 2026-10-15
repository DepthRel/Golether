using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;

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
    private const double TRACK_HEIGHT = 4;

    /// <summary>
    /// The bar under the track: the parts that can be played without waiting.
    /// </summary>
    private readonly RangeBar _bar = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Height = TRACK_HEIGHT };

    /// <summary>
    /// The track of the slider, or <see langword="null"/> before the template is applied.
    /// </summary>
    private Track? _track;

    /// <summary>
    /// Whether a press beside the track is being dragged along the timeline.
    /// </summary>
    private bool _draggingBeside;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimelineSlider"/> class.
    /// </summary>
    public TimelineSlider()
    {
        AddHandler(PointerPressedEvent, OnPressedBeside, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnMovedBeside, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, (_, _) => _draggingBeside = false, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, (_, _) => _draggingBeside = false, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
    }

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
        _track = track;
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

    /// <summary>
    /// A press a little above or below the track counts as a press on the track: the slider itself moves only for
    /// presses inside the track row and merely looks pressed for the rest of its height, so the user would have to
    /// aim at a thin line.
    /// </summary>
    /// <param name="sender">The slider.</param>
    /// <param name="e">The event data.</param>
    private void OnPressedBeside(object? sender, PointerPressedEventArgs e)
    {
        if (_track is not { } track ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            (e.Source as Visual)?.GetSelfAndVisualAncestors().Contains(track) == true)
        {
            return;
        }

        _draggingBeside = true;
        e.Pointer.Capture(this);
        MoveTo(e.GetPosition(track).X);
    }

    /// <summary>
    /// Keeps the thumb under the pointer while a press beside the track is dragged.
    /// </summary>
    /// <param name="sender">The slider.</param>
    /// <param name="e">The event data.</param>
    private void OnMovedBeside(object? sender, PointerEventArgs e)
    {
        if (_draggingBeside && _track is { } track)
        {
            MoveTo(e.GetPosition(track).X);
        }
    }

    /// <summary>
    /// Puts the middle of the thumb at a horizontal position on the track, the way the slider does for a press on
    /// the track itself.
    /// </summary>
    /// <param name="x">The position on the track.</param>
    private void MoveTo(double x)
    {
        if (_track is not { } track)
        {
            return;
        }

        var thumb = track.Thumb?.Bounds.Width ?? 0;
        var length = track.Bounds.Width - thumb;
        if (length <= 0)
        {
            return;
        }

        var fraction = Math.Clamp((x - (thumb / 2)) / length, 0, 1);
        SetCurrentValue(ValueProperty, Minimum + (fraction * (Maximum - Minimum)));
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
