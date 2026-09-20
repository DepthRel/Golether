using System.Globalization;
using Avalonia.Data.Converters;

namespace Golether.UI.Controls;

/// <summary>
/// Picks one of two texts by a boolean; the parameter is <c>text when true|text when false</c>.
/// </summary>
public sealed class BoolTextConverter : IValueConverter
{
    /// <summary>
    /// The shared instance.
    /// </summary>
    public static readonly BoolTextConverter Instance = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? string.Empty).Split('|');
        return value is true ? parts[0] : parts.Length > 1 ? parts[1] : string.Empty;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
