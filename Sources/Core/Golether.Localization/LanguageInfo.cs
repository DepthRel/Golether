using System.Globalization;

namespace Golether.Localization;

/// <summary>
/// Describes a language the application can be shown in.
/// </summary>
/// <param name="Code">The lower-case ISO 639-1 code (<c>ru</c>, <c>en</c>); it is also the name of the flag asset.</param>
/// <param name="NativeName">The name of the language in that language (<c>Русский</c>, <c>English</c>).</param>
public sealed record LanguageInfo(string Code, string NativeName)
{
    /// <summary>
    /// Gets the culture used to format numbers and dates in this language.
    /// </summary>
    public CultureInfo Culture => CultureInfo.GetCultureInfo(Code);
}
