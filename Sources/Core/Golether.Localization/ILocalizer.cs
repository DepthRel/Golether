using System.Globalization;

namespace Golether.Localization;

/// <summary>
/// Gives the text for a key in the language the user chose.
/// </summary>
public interface ILocalizer
{
    /// <summary>
    /// Raised after the language changed.
    /// </summary>
    event EventHandler? LanguageChanged;

    /// <summary>
    /// Gets the languages that can be chosen.
    /// </summary>
    IReadOnlyList<LanguageInfo> Languages { get; }

    /// <summary>
    /// Gets the language in use.
    /// </summary>
    LanguageInfo Current { get; }

    /// <summary>
    /// Gets the culture that formats numbers and dates in the language in use.
    /// </summary>
    CultureInfo Culture { get; }

    /// <summary>
    /// Chooses the language that matches a culture, or the default language when there are no texts for it.
    /// </summary>
    /// <param name="culture">The culture, for example the one of the operating system.</param>
    /// <returns>The language a first start should use.</returns>
    LanguageInfo Detect(CultureInfo culture);

    /// <summary>
    /// Switches the language.
    /// </summary>
    /// <param name="code">The language code.</param>
    /// <exception cref="ArgumentException">There are no texts for the language.</exception>
    void SetLanguage(string code);

    /// <summary>
    /// Returns a text.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The text; the key itself when no language has it, so a gap is visible and never breaks the screen.</returns>
    string Get(string key);

    /// <summary>
    /// Returns a text with values put in place of <c>{0}</c>, <c>{1}</c>…, formatted by the culture of the language.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="args">The values.</param>
    /// <returns>The text.</returns>
    string Format(string key, params object?[] args);
}
