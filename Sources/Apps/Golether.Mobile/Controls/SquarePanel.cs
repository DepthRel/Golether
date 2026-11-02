using Avalonia;
using Avalonia.Controls;

namespace Golether.Mobile.Controls;

/// <summary>
/// A panel as tall as it is wide: an item of the grid of pictures takes its share of the row and stays square.
/// </summary>
public sealed class SquarePanel : Panel
{
    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var side = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var size = new Size(side, side);

        foreach (var child in Children)
        {
            child.Measure(size);
        }

        return size;
    }
}
