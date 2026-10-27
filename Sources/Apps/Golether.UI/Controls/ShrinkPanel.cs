using Avalonia;
using Avalonia.Controls;

namespace Golether.UI.Controls;

/// <summary>
/// A horizontal row that gives up its least needed items when it has no room, instead of drawing them over its
/// neighbours: items with the highest <see cref="DropOrderProperty"/> leave first, items with none never do. The last
/// item takes whatever width is left (a text that shortens itself, for example). Nothing is drawn beyond the row.
/// </summary>
public sealed class ShrinkPanel : Panel
{
    /// <summary>
    /// How readily an item leaves the row: <c>0</c> never, otherwise the higher the earlier.
    /// </summary>
    public static readonly AttachedProperty<int> DropOrderProperty =
        AvaloniaProperty.RegisterAttached<ShrinkPanel, Control, int>("DropOrder");

    /// <summary>
    /// The items given up in the last measure.
    /// </summary>
    private readonly HashSet<Control> _dropped = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ShrinkPanel"/> class.
    /// </summary>
    public ShrinkPanel()
    {
        ClipToBounds = true;
    }

    /// <summary>
    /// Gets how readily an item leaves the row.
    /// </summary>
    /// <param name="control">The item.</param>
    /// <returns><c>0</c> for never, otherwise the higher the earlier.</returns>
    public static int GetDropOrder(Control control) => control.GetValue(DropOrderProperty);

    /// <summary>
    /// Sets how readily an item leaves the row.
    /// </summary>
    /// <param name="control">The item.</param>
    /// <param name="value"><c>0</c> for never, otherwise the higher the earlier.</param>
    public static void SetDropOrder(Control control, int value) => control.SetValue(DropOrderProperty, value);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        _dropped.Clear();
        var last = Children.LastOrDefault(c => c.IsVisible);
        var kept = new List<Control>();
        var height = 0.0;
        foreach (var child in Children)
        {
            if (child.IsVisible && !ReferenceEquals(child, last))
            {
                child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                kept.Add(child);
                height = Math.Max(height, child.DesiredSize.Height);
            }
        }

        // The least needed items leave until the rest fits; among equals the later one goes first.
        var width = kept.Sum(c => c.DesiredSize.Width);
        while (width > availableSize.Width &&
            kept.Where(c => GetDropOrder(c) > 0).OrderByDescending(GetDropOrder).ThenByDescending(kept.IndexOf).FirstOrDefault() is { } dropped)
        {
            kept.Remove(dropped);
            _dropped.Add(dropped);
            width -= dropped.DesiredSize.Width;
        }

        if (last is not null)
        {
            last.Measure(new Size(Math.Max(0, availableSize.Width - width), availableSize.Height));
            width += last.DesiredSize.Width;
            height = Math.Max(height, last.DesiredSize.Height);
        }

        return new Size(Math.Min(width, availableSize.Width), height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        var last = Children.LastOrDefault(c => c.IsVisible);
        foreach (var child in Children)
        {
            // An item given up keeps no place and is neither seen nor clicked, but stays measured for the next time.
            var dropped = _dropped.Contains(child);
            child.Opacity = dropped ? 0 : 1;
            child.IsHitTestVisible = !dropped;
            if (dropped || !child.IsVisible)
            {
                child.Arrange(default);
                continue;
            }

            var width = ReferenceEquals(child, last) ? Math.Max(0, finalSize.Width - x) : child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }
}
