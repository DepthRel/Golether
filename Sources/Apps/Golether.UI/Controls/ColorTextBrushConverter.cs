using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Golether.UI.Controls;

/// <summary>
/// Converts a <c>#RRGGBB</c> string to a brush.
/// </summary>
public sealed class ColorTextBrushConverter : IValueConverter
{
    /// <summary>
    /// The shared instance.
    /// </summary>
    public static readonly ColorTextBrushConverter Instance = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string text && Color.TryParse(text, out var color) ? new SolidColorBrush(color) : null;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
