using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;

namespace Golether.UI.Controls;

/// <summary>
/// Places children horizontally at a fraction of the panel width (participant markers on the timeline). Children
/// without a fraction are hidden.
/// </summary>
public sealed class FractionPanel : Panel
{
    /// <summary>
    /// The horizontal position of a child, 0–1, or <see langword="null"/> to hide it.
    /// </summary>
    public static readonly AttachedProperty<double?> FractionProperty =
        AvaloniaProperty.RegisterAttached<FractionPanel, Control, double?>("Fraction");

    /// <summary>
    /// Initializes static members of the <see cref="FractionPanel"/> class.
    /// </summary>
    static FractionPanel()
    {
        AffectsParentArrange<FractionPanel>(FractionProperty);
    }

    /// <summary>
    /// Gets the fraction of a child.
    /// </summary>
    /// <param name="control">The child.</param>
    /// <returns>The fraction.</returns>
    public static double? GetFraction(Control control) => control.GetValue(FractionProperty);

    /// <summary>
    /// Sets the fraction of a child.
    /// </summary>
    /// <param name="control">The child.</param>
    /// <param name="value">The fraction.</param>
    public static void SetFraction(Control control, double? value) => control.SetValue(FractionProperty, value);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0.0;
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var target = child is ContentPresenter { Child: { } inner } ? inner : child;
            var fraction = GetFraction(child) ?? GetFraction(target);
            if (fraction is not { } value || double.IsNaN(value))
            {
                child.Arrange(new Rect(-10_000, 0, child.DesiredSize.Width, child.DesiredSize.Height));
                continue;
            }

            var x = (Math.Clamp(value, 0, 1) * finalSize.Width) - (child.DesiredSize.Width / 2);
            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, child.DesiredSize.Height));
        }

        return finalSize;
    }
}
