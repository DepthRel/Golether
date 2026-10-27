using Avalonia;
using Avalonia.Controls;

namespace Golether.UI.Controls;

/// <summary>
/// Shows the first of its children that fits into the width it is given, and only that one: a long text gives way to
/// a shorter one instead of pushing its neighbours. When none fits, the last one is shown.
/// </summary>
public sealed class FallbackPanel : Panel
{
    /// <summary>
    /// The child shown after the last measure, or <see langword="null"/>.
    /// </summary>
    private Control? _shown;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        _shown = null;
        Control? last = null;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            if (!child.IsVisible)
            {
                continue;
            }

            last = child;
            if (_shown is null && child.DesiredSize.Width <= availableSize.Width)
            {
                _shown = child;
            }
        }

        _shown ??= last;
        return _shown is null ? default : new Size(Math.Min(_shown.DesiredSize.Width, availableSize.Width), _shown.DesiredSize.Height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            // A child that is not shown keeps no place and is not seen or clicked, but stays measured for the next time.
            var shown = ReferenceEquals(child, _shown);
            child.Opacity = shown ? 1 : 0;
            child.IsHitTestVisible = shown;
            child.Arrange(shown ? new Rect(0, 0, child.DesiredSize.Width, finalSize.Height) : default);
        }

        return finalSize;
    }
}
