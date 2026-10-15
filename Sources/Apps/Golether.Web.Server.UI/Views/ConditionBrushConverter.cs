using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Golether.Web.Server.UI.Enums;

namespace Golether.Web.Server.UI.Views;

/// <summary>
/// Gives the colour of the status: green when the server works, amber while it changes or runs outside the service,
/// red when it does not run, grey when nothing is known yet. The colours are those of the Golether theme.
/// </summary>
public sealed class ConditionBrushConverter : IValueConverter
{
    /// <summary>
    /// Gets the shared instance.
    /// </summary>
    public static ConditionBrushConverter Instance { get; } = new();

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => new SolidColorBrush(Color.Parse(value switch
        {
            ServerCondition.Good => "#62C28E",
            ServerCondition.Warning => "#E6C34A",
            ServerCondition.Bad => "#E0685A",
            _ => "#7F9491",
        }));

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
