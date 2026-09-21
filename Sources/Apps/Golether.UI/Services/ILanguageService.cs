using Golether.Localization;

namespace Golether.UI.Services;

/// <summary>
/// Chooses, applies and remembers the language of the user interface.
/// </summary>
public interface ILanguageService
{
    /// <summary>
    /// Gets the languages that can be chosen.
    /// </summary>
    IReadOnlyList<LanguageInfo> Languages { get; }

    /// <summary>
    /// Gets the language in use.
    /// </summary>
    LanguageInfo Current { get; }

    /// <summary>
    /// Applies the language stored in the database. On the first start nothing is stored yet: the language of the
    /// operating system is taken when there are texts for it (otherwise the default language) and stored.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the language is applied.</returns>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Switches the language and stores the choice.
    /// </summary>
    /// <param name="code">The language code.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the choice is stored.</returns>
    /// <exception cref="ArgumentException">There are no texts for the language.</exception>
    Task SelectAsync(string code, CancellationToken cancellationToken);
}
