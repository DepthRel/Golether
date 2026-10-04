using Avalonia;
using Avalonia.Controls;

namespace Golether.UI.Controls;

/// <summary>
/// A panel as high as its width times <see cref="HeightPerWidth"/>; every child fills it. The camera picture of a
/// participant tile sits in one, so the tile grows with the side panel and keeps its proportions.
/// </summary>
public sealed class AspectPanel : Panel
{
    /// <summary>
    /// The height of the panel for a width of <c>1</c>.
    /// </summary>
    public static readonly StyledProperty<double> HeightPerWidthProperty =
        AvaloniaProperty.Register<AspectPanel, double>(nameof(HeightPerWidth), 1);

    /// <summary>
    /// Initializes static members of the <see cref="AspectPanel"/> class.
    /// </summary>
    static AspectPanel() => AffectsMeasure<AspectPanel>(HeightPerWidthProperty);

    /// <summary>
    /// Gets or sets the height of the panel for a width of <c>1</c>.
    /// </summary>
    public double HeightPerWidth
    {
        get => GetValue(HeightPerWidthProperty);
        set => SetValue(HeightPerWidthProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // Without a width to follow (an endless row) the panel has nothing to keep proportions to and takes none.
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 0;
        var size = new Size(width, width * Math.Max(0, HeightPerWidth));
        foreach (var child in Children)
        {
            child.Measure(size);
        }

        return size;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = new Size(finalSize.Width, finalSize.Width * Math.Max(0, HeightPerWidth));
        foreach (var child in Children)
        {
            child.Arrange(new Rect(size));
        }

        return size;
    }
}
