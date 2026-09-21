namespace Golether.Localization;

/// <summary>
/// The texts of one language: the strategy the localizer delegates every lookup to.
/// </summary>
public interface ILanguagePack
{
    /// <summary>
    /// Gets the language of this pack.
    /// </summary>
    LanguageInfo Language { get; }

    /// <summary>
    /// Gets the keys of the texts this pack has.
    /// </summary>
    IReadOnlyCollection<string> Keys { get; }

    /// <summary>
    /// Looks a text up.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="text">The text, when found.</param>
    /// <returns><see langword="true"/> when the pack has the key.</returns>
    bool TryGet(string key, out string text);
}
