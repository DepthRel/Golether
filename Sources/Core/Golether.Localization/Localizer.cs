using System.Globalization;

namespace Golether.Localization;

/// <summary>
/// Looks texts up in the pack of the current language and falls back to the default language, so a missing
/// translation shows the default text instead of a gap. It never asks which language is in use.
/// </summary>
public sealed class Localizer : ILocalizer
{
    /// <summary>
    /// The catalog of languages.
    /// </summary>
    private readonly LanguageCatalog _catalog;

    /// <summary>
    /// The pack of the current language.
    /// </summary>
    private volatile ILanguagePack _current;

    /// <summary>
    /// Initializes a new instance of the <see cref="Localizer"/> class.
    /// </summary>
    /// <param name="catalog">The languages.</param>
    /// <param name="code">The code of the first language, or <see langword="null"/> for the default one.</param>
    public Localizer(LanguageCatalog catalog, string? code = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _current = catalog.Find(code) ?? catalog.Default;
    }

    /// <inheritdoc />
    public event EventHandler? LanguageChanged;

    /// <inheritdoc />
    public IReadOnlyList<LanguageInfo> Languages => _catalog.Languages;

    /// <inheritdoc />
    public LanguageInfo Current => _current.Language;

    /// <inheritdoc />
    public CultureInfo Culture => _current.Language.Culture;

    /// <inheritdoc />
    public LanguageInfo Detect(CultureInfo culture) => _catalog.Detect(culture);

    /// <inheritdoc />
    public void SetLanguage(string code)
    {
        var pack = _catalog.Find(code) ?? throw new ArgumentException($"There are no texts for the language '{code}'.", nameof(code));
        if (ReferenceEquals(pack, _current))
        {
            return;
        }

        _current = pack;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public string Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _current.TryGet(key, out var text) || _catalog.Default.TryGet(key, out text) ? text : key;
    }

    /// <inheritdoc />
    public string Format(string key, params object?[] args) => string.Format(Culture, Get(key), args);
}
