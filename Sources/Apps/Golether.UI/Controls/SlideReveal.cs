using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;

namespace Golether.UI.Controls;

/// <summary>
/// A box whose content slides out of it and fades, and slides back in. The box itself keeps its size, so nothing
/// around it moves: it is made for the bars that lie over the video in full screen.
/// </summary>
public sealed class SlideReveal : Decorator
{
    /// <summary>
    /// Whether the content is shown.
    /// </summary>
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<SlideReveal, bool>(nameof(IsExpanded), true);

    /// <summary>
    /// How much of the content is on show: <c>1</c> all of it, <c>0</c> nothing. It is animated, and the content is
    /// placed from it.
    /// </summary>
    public static readonly StyledProperty<double> ShownProperty =
        AvaloniaProperty.Register<SlideReveal, double>(nameof(Shown), 1);

    /// <summary>
    /// Whether the content leaves upwards (a bar at the top) instead of downwards (a bar at the bottom).
    /// </summary>
    public static readonly StyledProperty<bool> SlideUpProperty =
        AvaloniaProperty.Register<SlideReveal, bool>(nameof(SlideUp));

    /// <summary>
    /// Gets or sets how long sliding in and out takes. Tests set it to zero to get the settled state at once.
    /// </summary>
    public static TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(220);

    /// <summary>
    /// Initializes static members of the <see cref="SlideReveal"/> class.
    /// </summary>
    static SlideReveal()
    {
        AffectsArrange<SlideReveal>(ShownProperty, SlideUpProperty);
        ClipToBoundsProperty.OverrideDefaultValue<SlideReveal>(true);
        IsExpandedProperty.Changed.AddClassHandler<SlideReveal>((box, _) => box.Animate());
        ShownProperty.Changed.AddClassHandler<SlideReveal>((box, _) => box.Opacity = Math.Clamp(box.Shown, 0, 1));
    }

    /// <summary>
    /// Gets or sets a value indicating whether the content is shown.
    /// </summary>
    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>
    /// Gets or sets how much of the content is on show, from <c>0</c> to <c>1</c>.
    /// </summary>
    public double Shown
    {
        get => GetValue(ShownProperty);
        set => SetValue(ShownProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the content leaves upwards instead of downwards.
    /// </summary>
    public bool SlideUp
    {
        get => GetValue(SlideUpProperty);
        set => SetValue(SlideUpProperty, value);
    }

    /// <summary>
    /// Shows or hides the content at once, without the slide: for the moment the box moves to another place.
    /// </summary>
    /// <param name="expanded">Whether the content is shown.</param>
    public void Snap(bool expanded)
    {
        var transitions = Transitions;
        Transitions = null;
        IsExpanded = expanded;
        Shown = expanded ? 1 : 0;
        Transitions = transitions;
    }

    /// <summary>
    /// Starts moving towards the state asked for by <see cref="IsExpanded"/>.
    /// </summary>
    private void Animate()
    {
        if (Duration > TimeSpan.Zero && Transitions is null)
        {
            Transitions =
            [
                new DoubleTransition { Property = ShownProperty, Duration = Duration, Easing = new CubicEaseOut() },
            ];
        }

        Shown = IsExpanded ? 1 : 0;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is { } child)
        {
            var hidden = finalSize.Height * (1 - Math.Clamp(Shown, 0, 1));
            child.Arrange(new Rect(0, SlideUp ? -hidden : hidden, finalSize.Width, finalSize.Height));
        }

        return finalSize;
    }
}
