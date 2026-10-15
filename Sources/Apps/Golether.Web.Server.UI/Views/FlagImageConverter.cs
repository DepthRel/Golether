using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Golether.Localization;

namespace Golether.Web.Server.UI.Views;

/// <summary>
/// Shows the flag of a language: the image <c>Assets/Flags/&lt;language code&gt;.png</c>, the same files the
/// application uses. A language without a flag is shown with its name only.
/// </summary>
public sealed class FlagImageConverter : IValueConverter
{
    /// <summary>
    /// The flags already loaded, by language code; <see langword="null"/> marks a language without a flag.
    /// </summary>
    private readonly ConcurrentDictionary<string, IImage?> _flags = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the shared instance.
    /// </summary>
    public static FlagImageConverter Instance { get; } = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is LanguageInfo language ? _flags.GetOrAdd(language.Code, Load) : null;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>
    /// Loads the flag of a language.
    /// </summary>
    /// <param name="code">The language code.</param>
    /// <returns>The flag, or <see langword="null"/> when the monitor has no image for the language.</returns>
    private static IImage? Load(string code)
    {
        try
        {
            var uri = new Uri($"avares://golether-server-monitor/Assets/Flags/{Uri.EscapeDataString(code)}.png");
            if (!AssetLoader.Exists(uri))
            {
                return null;
            }

            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UriFormatException or InvalidOperationException)
        {
            return null;
        }
    }
}
