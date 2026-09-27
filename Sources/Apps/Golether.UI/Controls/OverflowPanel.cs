using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;

namespace Golether.UI.Controls;

/// <summary>
/// A horizontal toolbar that folds some items into a "more" button when the row does not fit.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Items with <see cref="CanOverflowProperty"/> leave the row together when the row is too wide; they get the
/// <c>overflowed</c> class and take no space.</item>
/// <item>The item with <see cref="IsOverflowButtonProperty"/> is shown only while items are folded.</item>
/// <item><see cref="ReservedWidth"/> keeps room for the content to the left of the toolbar.</item>
/// </list>
/// </remarks>
public sealed class OverflowPanel : Panel
{
    /// <summary>
    /// Whether an item may be folded.
    /// </summary>
    public static readonly AttachedProperty<bool> CanOverflowProperty =
        AvaloniaProperty.RegisterAttached<OverflowPanel, Control, bool>("CanOverflow");

    /// <summary>
    /// Whether an item is the "more" button.
    /// </summary>
    public static readonly AttachedProperty<bool> IsOverflowButtonProperty =
        AvaloniaProperty.RegisterAttached<OverflowPanel, Control, bool>("IsOverflowButton");

    /// <summary>
    /// How far an item is folded away: <c>0</c> fully shown, <c>1</c> gone. It is animated, and the row is laid out
    /// from it, so folding and unfolding slides the neighbours instead of snapping them.
    /// </summary>
    public static readonly AttachedProperty<double> FoldProperty =
        AvaloniaProperty.RegisterAttached<OverflowPanel, Control, double>("Fold");

    /// <summary>
    /// The state the item was last told to reach. Kept apart from <see cref="FoldProperty"/>, which is mid-animation
    /// most of the time, so the target is set once per change instead of on every layout pass.
    /// </summary>
    private static readonly AttachedProperty<bool> FoldedProperty =
        AvaloniaProperty.RegisterAttached<OverflowPanel, Control, bool>("Folded");

    /// <summary>
    /// The space between items.
    /// </summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<OverflowPanel, double>(nameof(Spacing), 8);

    /// <summary>
    /// The width kept free for other content.
    /// </summary>
    public static readonly StyledProperty<double> ReservedWidthProperty =
        AvaloniaProperty.Register<OverflowPanel, double>(nameof(ReservedWidth));

    /// <summary>
    /// Whether items are folded.
    /// </summary>
    public static readonly DirectProperty<OverflowPanel, bool> IsOverflowingProperty =
        AvaloniaProperty.RegisterDirect<OverflowPanel, bool>(nameof(IsOverflowing), p => p.IsOverflowing);

    /// <summary>
    /// The class given to folded items.
    /// </summary>
    public const string OverflowedClass = "overflowed";

    /// <summary>
    /// The extra room the row must win back before folded items return. Folding and unfolding at the very same width
    /// makes the toolbar flicker while the window is dragged across the threshold; this dead band keeps it still.
    /// </summary>
    private const double Hysteresis = 24;

    /// <summary>
    /// Gets or sets how long folding and unfolding takes. Tests set it to zero to get the settled row at once.
    /// </summary>
    public static TimeSpan FoldDuration { get; set; } = TimeSpan.FromMilliseconds(160);

    /// <summary>
    /// Backing field of <see cref="IsOverflowing"/>.
    /// </summary>
    private bool _isOverflowing;

    /// <summary>
    /// Whether the row has been laid out once. The first pass takes its shape without animating into it.
    /// </summary>
    private bool _laidOut;

    /// <summary>
    /// Whether a measure pass is running. The targets are set from inside it, and that pass already takes them into
    /// account, so it must not ask for another one.
    /// </summary>
    private bool _measuring;

    /// <summary>
    /// Initializes static members of the <see cref="OverflowPanel"/> class.
    /// </summary>
    static OverflowPanel()
    {
        AffectsMeasure<OverflowPanel>(SpacingProperty, ReservedWidthProperty);

        // Every step of the fold animation re-lays out the row, which is what makes the neighbours glide.
        FoldProperty.Changed.AddClassHandler<Control>((child, _) =>
        {
            if (child.Parent is OverflowPanel { _measuring: false } panel)
            {
                panel.InvalidateMeasure();
                panel.InvalidateArrange();
            }
        });
    }

    /// <summary>
    /// Gets or sets the space between items.
    /// </summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>
    /// Gets or sets the width kept free for other content.
    /// </summary>
    public double ReservedWidth
    {
        get => GetValue(ReservedWidthProperty);
        set => SetValue(ReservedWidthProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether items are folded into the "more" button.
    /// </summary>
    public bool IsOverflowing
    {
        get => _isOverflowing;
        private set => SetAndRaise(IsOverflowingProperty, ref _isOverflowing, value);
    }

    /// <summary>
    /// Gets whether an item may be folded.
    /// </summary>
    /// <param name="control">The item.</param>
    /// <returns>The value.</returns>
    public static bool GetCanOverflow(Control control) => control.GetValue(CanOverflowProperty);

    /// <summary>
    /// Sets whether an item may be folded.
    /// </summary>
    /// <param name="control">The item.</param>
    /// <param name="value">The value.</param>
    public static void SetCanOverflow(Control control, bool value) => control.SetValue(CanOverflowProperty, value);

    /// <summary>
    /// Gets whether an item is the "more" button.
    /// </summary>
    /// <param name="control">The item.</param>
    /// <returns>The value.</returns>
    public static bool GetIsOverflowButton(Control control) => control.GetValue(IsOverflowButtonProperty);

    /// <summary>
    /// Sets whether an item is the "more" button.
    /// </summary>
    /// <param name="control">The item.</param>
    /// <param name="value">The value.</param>
    public static void SetIsOverflowButton(Control control, bool value) => control.SetValue(IsOverflowButtonProperty, value);

    /// <summary>
    /// Gets how far an item is folded away.
    /// </summary>
    /// <param name="control">The item.</param>
    /// <returns>The value between <c>0</c> and <c>1</c>.</returns>
    public static double GetFold(Control control) => control.GetValue(FoldProperty);

    /// <summary>
    /// Sets how far an item is folded away.
    /// </summary>
    /// <param name="control">The item.</param>
    /// <param name="value">The value between <c>0</c> and <c>1</c>.</param>
    public static void SetFold(Control control, double value) => control.SetValue(FoldProperty, value);

    /// <summary>
    /// Gives an item the animation of <see cref="FoldProperty"/>, once.
    /// </summary>
    /// <param name="control">The item.</param>
    private static void EnsureFoldAnimated(Control control)
    {
        if (FoldDuration <= TimeSpan.Zero)
        {
            return;
        }

        control.Transitions ??= [];
        foreach (var transition in control.Transitions)
        {
            if (transition is DoubleTransition existing && existing.Property == FoldProperty)
            {
                return;
            }
        }

        control.Transitions.Add(new DoubleTransition
        {
            Property = FoldProperty,
            Duration = FoldDuration,
            Easing = new CubicEaseOut(),
        });
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        _measuring = true;
        try
        {
            return MeasureRow(availableSize);
        }
        finally
        {
            _measuring = false;
        }
    }

    /// <summary>
    /// Works out the folding and the width of the row.
    /// </summary>
    /// <param name="availableSize">The room offered to the row.</param>
    /// <returns>The size the row wants.</returns>
    private Size MeasureRow(Size availableSize)
    {
        var infinite = new Size(double.PositiveInfinity, availableSize.Height);
        double full = 0, height = 0;
        foreach (var child in Children)
        {
            // Folded items stay visible and are measured as if shown, so the panel knows when they fit again.
            child.Measure(infinite);
            height = Math.Max(height, child.DesiredSize.Height);
            if (!GetIsOverflowButton(child) && child.DesiredSize.Width > 0)
            {
                full += child.DesiredSize.Width + Spacing;
            }
        }

        var available = Math.Max(0, availableSize.Width - ReservedWidth);
        var needed = full - Spacing;

        // Folded rows need a little more room than they gave up before they unfold again, so a width that hovers
        // around the threshold does not make the toolbar jump back and forth.
        var overflowing = _isOverflowing ? needed > available - Hysteresis : needed > available;
        IsOverflowing = overflowing;
        foreach (var child in Children)
        {
            // The class only hides items from the pointer and the keyboard; the fold takes care of size and opacity.
            var folded = GetIsOverflowButton(child) ? !overflowing : overflowing && GetCanOverflow(child);
            child.Classes.Set(OverflowedClass, folded);
            if (child.GetValue(FoldedProperty) == folded)
            {
                continue;
            }

            child.SetValue(FoldedProperty, folded);
            if (_laidOut)
            {
                EnsureFoldAnimated(child);
            }

            SetFold(child, folded ? 1 : 0);
        }

        // The width follows the running animation, so whatever sits beside the toolbar glides along with it.
        var width = 0.0;
        foreach (var child in Children)
        {
            var shown = Shown(child);
            if (child.DesiredSize.Width > 0 && shown > 0)
            {
                width += (child.DesiredSize.Width + Spacing) * shown;
            }
        }

        _laidOut = true;
        return new Size(Math.Min(Math.Max(0, width - Spacing), availableSize.Width), height);
    }

    /// <summary>
    /// Returns how much of an item is on show, from <c>0</c> (folded away) to <c>1</c>.
    /// </summary>
    /// <param name="child">The item.</param>
    /// <returns>The share.</returns>
    private static double Shown(Control child) => 1 - Math.Clamp(GetFold(child), 0, 1);

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        foreach (var child in Children)
        {
            // An item shrinks and fades away together, so it leaves the row instead of blinking out of it.
            var shown = Shown(child);
            var width = child.DesiredSize.Width * shown;
            child.Opacity = shown;
            if (width <= 0.5)
            {
                child.Arrange(new Rect(x, 0, 0, 0));
                continue;
            }

            var height = child.DesiredSize.Height;
            child.Arrange(new Rect(x, (finalSize.Height - height) / 2, width, height));
            x += width + (Spacing * shown);
        }

        return finalSize;
    }
}
