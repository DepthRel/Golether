using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Golether.UI.Enums;

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
    private static readonly IBrush _good = new SolidColorBrush(Color.Parse("#62C28E"));

    /// <summary>
    /// The brush of values that need attention.
    /// </summary>
    private static readonly IBrush _warning = new SolidColorBrush(Color.Parse("#E6C34A"));

    /// <summary>
    /// The brush of critical values.
    /// </summary>
    private static readonly IBrush _critical = new SolidColorBrush(Color.Parse("#E0685A"));

    /// <summary>
    /// The brush of neutral values.
    /// </summary>
    private static readonly IBrush _neutral = new SolidColorBrush(Color.Parse("#DDE7E5"));

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            IndicatorLevel.Good => _good,
            IndicatorLevel.Warning => _warning,
            IndicatorLevel.Critical => _critical,
            _ => _neutral,
        };

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
