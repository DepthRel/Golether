using System.Globalization;
using Avalonia.Data.Converters;
using Golether.Localization;

namespace Golether.Web.Server.UI.Views;

/// <summary>
/// Words a level of the log for the user, with its name in the file in brackets: <c>Information</c> becomes
/// "Usual events (Information)".
/// </summary>
public sealed class LogLevelNameConverter : IValueConverter
{
    /// <summary>
    /// Gets the shared instance.
    /// </summary>
    public static LogLevelNameConverter Instance { get; } = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string level ? Texts.Get("Monitor.LogLevel." + level) : value;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
