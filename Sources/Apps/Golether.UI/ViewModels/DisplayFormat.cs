using System.Globalization;
using Golether.Localization;

namespace Golether.UI.ViewModels;

/// <summary>
/// Formats values for the UI by the conventions of the language in use: the decimal mark, the digit grouping and the
/// names of the units.
/// </summary>
public static class DisplayFormat
{
    /// <summary>
    /// Gets the culture of the language in use, which formats numbers.
    /// </summary>
    private static CultureInfo Culture => Texts.Localizer.Culture;

    /// <summary>
    /// Formats a media position as <c>h:mm:ss</c>.
    /// </summary>
    /// <param name="value">The position.</param>
    /// <returns>The text.</returns>
    public static string Position(TimeSpan? value)
        => value is { } position
            ? ((int)position.TotalHours).ToString(CultureInfo.InvariantCulture) + position.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture)
            : "0:00:00";

    /// <summary>
    /// Formats a drift with sign: <c>+90 ms</c>, <c>−1.8 s</c>.
    /// </summary>
    /// <param name="drift">The drift.</param>
    /// <returns>The text.</returns>
    public static string Drift(TimeSpan drift)
    {
        var sign = drift < TimeSpan.Zero ? "−" : "+";
        var magnitude = drift.Duration();
        return magnitude < TimeSpan.FromSeconds(1)
            ? sign + Texts.Format("Format.Milliseconds", (int)magnitude.TotalMilliseconds)
            : sign + Texts.Format("Format.Seconds", magnitude.TotalSeconds.ToString("0.0", Culture));
    }

    /// <summary>
    /// Returns the level of a drift.
    /// </summary>
    /// <param name="drift">The drift.</param>
    /// <returns>Good below 250 ms, warning below 1 s, critical above.</returns>
    public static IndicatorLevel DriftLevel(TimeSpan drift)
        => drift.Duration() < TimeSpan.FromMilliseconds(250) ? IndicatorLevel.Good
            : drift.Duration() < TimeSpan.FromSeconds(1) ? IndicatorLevel.Warning
            : IndicatorLevel.Critical;

    /// <summary>
    /// Formats a round trip: <c>1 240 ms</c>.
    /// </summary>
    /// <param name="milliseconds">The round trip, or <see langword="null"/>.</param>
    /// <returns>The text.</returns>
    public static string Ping(int? milliseconds)
        => milliseconds is { } value ? Texts.Format("Format.Milliseconds", value.ToString("#,0", Culture)) : "—";

    /// <summary>
    /// Returns the level of a round trip.
    /// </summary>
    /// <param name="milliseconds">The round trip.</param>
    /// <returns>Good below 400 ms, warning below 1.5 s, critical above.</returns>
    public static IndicatorLevel PingLevel(int? milliseconds)
        => milliseconds switch
        {
            null => IndicatorLevel.Neutral,
            < 400 => IndicatorLevel.Good,
            < 1500 => IndicatorLevel.Warning,
            _ => IndicatorLevel.Critical,
        };

    /// <summary>
    /// Formats the buffered amount: <c>12 s</c>.
    /// </summary>
    /// <param name="cacheAhead">The buffered media.</param>
    /// <returns>The text.</returns>
    public static string Buffer(TimeSpan cacheAhead) => Texts.Format("Format.Seconds", (int)cacheAhead.TotalSeconds);

    /// <summary>
    /// Returns the level of the buffered amount.
    /// </summary>
    /// <param name="cacheAhead">The buffered media.</param>
    /// <param name="buffering">Whether the player waits for data.</param>
    /// <returns>Critical while buffering, warning below 5 s.</returns>
    public static IndicatorLevel BufferLevel(TimeSpan cacheAhead, bool buffering)
        => buffering ? IndicatorLevel.Critical : cacheAhead < TimeSpan.FromSeconds(5) ? IndicatorLevel.Warning : IndicatorLevel.Good;

    /// <summary>
    /// Formats a file size: <c>64.2 GB</c>.
    /// </summary>
    /// <param name="bytes">The size in bytes.</param>
    /// <returns>The text.</returns>
    public static string Size(long bytes)
    {
        string[] units =
        [
            Texts.Get("Format.Unit.Byte"),
            Texts.Get("Format.Unit.Kilobyte"),
            Texts.Get("Format.Unit.Megabyte"),
            Texts.Get("Format.Unit.Gigabyte"),
            Texts.Get("Format.Unit.Terabyte"),
        ];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value.ToString(unit == 0 ? "0" : "0.0", Culture)} {units[unit]}";
    }
}
