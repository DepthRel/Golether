using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Golether.UI.ViewModels;

namespace Golether.UI.Controls;

/// <summary>
/// Converts an <see cref="IndicatorLevel"/> to the theme color.
/// </summary>
public sealed class IndicatorLevelBrushConverter : IValueConverter
{
    /// <summary>
    /// The shared instance.
    /// </summary>
    public static readonly IndicatorLevelBrushConverter Instance = new();

    /// <summary>
    /// The brush of healthy values.
    /// </summary>
    private static readonly IBrush Good = new SolidColorBrush(Color.Parse("#62C28E"));

    /// <summary>
    /// The brush of values that need attention.
    /// </summary>
    private static readonly IBrush Warning = new SolidColorBrush(Color.Parse("#E6C34A"));

    /// <summary>
    /// The brush of critical values.
    /// </summary>
    private static readonly IBrush Critical = new SolidColorBrush(Color.Parse("#E0685A"));

    /// <summary>
    /// The brush of neutral values.
    /// </summary>
    private static readonly IBrush Neutral = new SolidColorBrush(Color.Parse("#DDE7E5"));

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            IndicatorLevel.Good => Good,
            IndicatorLevel.Warning => Warning,
            IndicatorLevel.Critical => Critical,
            _ => Neutral,
        };

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

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
