using Avalonia.Media;
using Golether.Localization;

namespace Golether.UI.ViewModels;

/// <summary>
/// A language in the language selector: the flag and the name of the language in that language.
/// </summary>
public sealed class LanguageOption
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LanguageOption"/> class.
    /// </summary>
    /// <param name="language">The language.</param>
    /// <param name="flag">The flag, or <see langword="null"/> when there is no image for it.</param>
    public LanguageOption(LanguageInfo language, IImage? flag)
    {
        Language = language ?? throw new ArgumentNullException(nameof(language));
        Flag = flag;
    }

    /// <summary>
    /// Gets the language.
    /// </summary>
    public LanguageInfo Language { get; }

    /// <summary>
    /// Gets the language code.
    /// </summary>
    public string Code => Language.Code;

    /// <summary>
    /// Gets the name of the language in that language.
    /// </summary>
    public string Name => Language.NativeName;

    /// <summary>
    /// Gets the flag, or <see langword="null"/>.
    /// </summary>
    public IImage? Flag { get; }
}
