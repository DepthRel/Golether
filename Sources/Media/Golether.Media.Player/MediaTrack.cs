using System.Globalization;
using Golether.Core.Data.Enums;

namespace Golether.Media.Player;

/// <summary>
/// A sound or subtitle track of the loaded file. The choice of tracks is local to each participant.
/// </summary>
/// <param name="Id">The track identifier within its kind.</param>
/// <param name="Kind">The kind.</param>
/// <param name="Title">The title from the file, if any.</param>
/// <param name="Language">The language code from the file (<c>rus</c>, <c>en</c>), if any.</param>
/// <param name="Codec">The codec name, if known.</param>
/// <param name="Channels">The number of sound channels, if known.</param>
/// <param name="IsDefault">Whether the file marks the track as default.</param>
/// <param name="IsExternal">Whether the track was loaded from a separate file.</param>
/// <param name="IsSelected">Whether the track is playing or shown now.</param>
public sealed record MediaTrack(
    long Id,
    MediaTrackKind Kind,
    string? Title,
    string? Language,
    string? Codec,
    int? Channels,
    bool IsDefault,
    bool IsExternal,
    bool IsSelected)
{
    /// <summary>
    /// Gets the readable language name, or <see langword="null"/>.
    /// </summary>
    public string? LanguageName => DescribeLanguage(Language);

    /// <summary>
    /// Gets the label shown in the track menu.
    /// </summary>
    public string DisplayName
    {
        get
        {
            var parts = new List<string>();
            var language = LanguageName;
            if (!string.IsNullOrWhiteSpace(Title))
            {
                parts.Add(Title.Trim());
                if (language is not null && !Title.Contains(language, StringComparison.OrdinalIgnoreCase))
                {
                    parts.Add(language);
                }
            }
            else
            {
                parts.Add(language ?? $"Дорожка {Id}");
            }

            if (Kind == MediaTrackKind.Audio && Channels is > 2)
            {
                parts.Add(Channels == 6 ? "5.1" : Channels == 8 ? "7.1" : $"{Channels} кан.");
            }

            if (!string.IsNullOrWhiteSpace(Codec))
            {
                parts.Add(Codec.ToUpperInvariant());
            }

            if (IsExternal)
            {
                parts.Add("из файла");
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// Turns a two- or three-letter language code into a name in the current UI language.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <returns>The name, or <see langword="null"/> when the code is empty or unknown.</returns>
    public static string? DescribeLanguage(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Equals("und", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var trimmed = code.Trim();
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
        {
            if (culture.TwoLetterISOLanguageName.Equals(trimmed, StringComparison.OrdinalIgnoreCase)
                || culture.ThreeLetterISOLanguageName.Equals(trimmed, StringComparison.OrdinalIgnoreCase)
                || (trimmed.Length == 3 && BibliographicCodes.TryGetValue(trimmed, out var two)
                    && culture.TwoLetterISOLanguageName.Equals(two, StringComparison.OrdinalIgnoreCase)))
            {
                if (culture.Name.Length == 0)
                {
                    continue;
                }

                var name = culture.NativeName;
                return name.Length > 0 ? char.ToUpper(name[0], CultureInfo.CurrentCulture) + name[1..] : trimmed;
            }
        }

        return trimmed;
    }

    /// <summary>
    /// Checks whether the track has a language (compares two- and three-letter codes).
    /// </summary>
    /// <param name="code">The preferred language code.</param>
    /// <returns><see langword="true"/> when the languages match.</returns>
    public bool HasLanguage(string? code)
        => !string.IsNullOrWhiteSpace(code) && Normalize(Language) is { } own && own == Normalize(code);

    /// <summary>
    /// Brings a language code to its two-letter form when it is known.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <returns>The normalized code, or <see langword="null"/>.</returns>
    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var trimmed = code.Trim().ToLowerInvariant();
        if (trimmed.Length == 3)
        {
            if (BibliographicCodes.TryGetValue(trimmed, out var two))
            {
                return two;
            }

            foreach (var culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
            {
                if (culture.Name.Length > 0 && culture.ThreeLetterISOLanguageName == trimmed)
                {
                    return culture.TwoLetterISOLanguageName;
                }
            }
        }

        return trimmed;
    }

    /// <summary>
    /// ISO 639-2/B codes that differ from the terminology codes .NET knows (common in Matroska files).
    /// </summary>
    private static readonly Dictionary<string, string> BibliographicCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ger"] = "de",
        ["fre"] = "fr",
        ["chi"] = "zh",
        ["cze"] = "cs",
        ["dut"] = "nl",
        ["gre"] = "el",
        ["per"] = "fa",
        ["rum"] = "ro",
        ["slo"] = "sk",
        ["arm"] = "hy",
        ["geo"] = "ka",
        ["ice"] = "is",
        ["mac"] = "mk",
        ["may"] = "ms",
        ["bur"] = "my",
        ["alb"] = "sq",
        ["wel"] = "cy",
        ["baq"] = "eu",
        ["tib"] = "bo",
    };
}
