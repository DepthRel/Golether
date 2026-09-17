using Avalonia;
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
    /// Backing field of <see cref="IsOverflowing"/>.
    /// </summary>
    private bool _isOverflowing;

    /// <summary>
    /// Initializes static members of the <see cref="OverflowPanel"/> class.
    /// </summary>
    static OverflowPanel()
    {
        AffectsMeasure<OverflowPanel>(SpacingProperty, ReservedWidthProperty);
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

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var infinite = new Size(double.PositiveInfinity, availableSize.Height);
        double full = 0, kept = 0, height = 0;
        Control? more = null;
        foreach (var child in Children)
        {
            // Folded items stay visible and are measured as if shown, so the panel knows when they fit again.
            child.Measure(infinite);
            height = Math.Max(height, child.DesiredSize.Height);
            if (GetIsOverflowButton(child))
            {
                more = child;
                continue;
            }

            if (child.DesiredSize.Width <= 0)
            {
                continue;
            }

            full += child.DesiredSize.Width + Spacing;
            if (!GetCanOverflow(child))
            {
                kept += child.DesiredSize.Width + Spacing;
            }
        }

        var available = Math.Max(0, availableSize.Width - ReservedWidth);
        var overflowing = full - Spacing > available;
        IsOverflowing = overflowing;
        foreach (var child in Children)
        {
            // The class only hides items from the pointer, the keyboard and the eye; it does not change sizes.
            var folded = GetIsOverflowButton(child) ? !overflowing : overflowing && GetCanOverflow(child);
            child.Classes.Set(OverflowedClass, folded);
        }

        var moreWidth = overflowing && more is not null ? more.DesiredSize.Width + Spacing : 0;
        var width = overflowing ? kept + moreWidth : full;
        return new Size(Math.Min(Math.Max(0, width - Spacing), availableSize.Width), height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        foreach (var child in Children)
        {
            var hidden = (IsOverflowing && GetCanOverflow(child)) || (GetIsOverflowButton(child) && !IsOverflowing);
            if (hidden || child.DesiredSize.Width <= 0)
            {
                child.Arrange(new Rect(x, 0, 0, 0));
                continue;
            }

            var height = child.DesiredSize.Height;
            child.Arrange(new Rect(x, (finalSize.Height - height) / 2, child.DesiredSize.Width, height));
            x += child.DesiredSize.Width + Spacing;
        }

        return finalSize;
    }
}
