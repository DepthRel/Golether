using System.Globalization;

namespace Golether.Localization;

/// <summary>
/// The languages the application has texts for.
/// </summary>
public sealed class LanguageCatalog
{
    /// <summary>
    /// The code of the language used when nothing better is known, and for texts a language lacks.
    /// </summary>
    public const string DefaultCode = "en";

    /// <summary>
    /// The prefix of the embedded language files.
    /// </summary>
    private const string ResourcePrefix = "Golether.Localization.Languages.";

    /// <summary>
    /// The packs by language code.
    /// </summary>
    private readonly Dictionary<string, ILanguagePack> _packs;

    /// <summary>
    /// Initializes a new instance of the <see cref="LanguageCatalog"/> class.
    /// </summary>
    /// <param name="packs">The language packs; one of them is <see cref="DefaultCode"/>.</param>
    /// <exception cref="ArgumentException">Two packs share a code or the default language is missing.</exception>
    public LanguageCatalog(IEnumerable<ILanguagePack> packs)
    {
        ArgumentNullException.ThrowIfNull(packs);
        _packs = [];
        foreach (var pack in packs)
        {
            if (!_packs.TryAdd(pack.Language.Code, pack))
            {
                throw new ArgumentException($"The language '{pack.Language.Code}' is defined twice.", nameof(packs));
            }
        }

        if (!_packs.ContainsKey(DefaultCode))
        {
            throw new ArgumentException($"The default language '{DefaultCode}' is missing.", nameof(packs));
        }

        Languages = [.. _packs.Values.Select(p => p.Language).OrderBy(l => l.NativeName, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Gets the languages in the order they are offered.
    /// </summary>
    public IReadOnlyList<LanguageInfo> Languages { get; }

    /// <summary>
    /// Gets the default language pack, the source of texts a language lacks.
    /// </summary>
    public ILanguagePack Default => _packs[DefaultCode];

    /// <summary>
    /// Reads every language file embedded in this assembly.
    /// </summary>
    /// <returns>The catalog.</returns>
    public static LanguageCatalog LoadEmbedded()
    {
        var assembly = typeof(LanguageCatalog).Assembly;
        var packs = new List<ILanguagePack>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            packs.Add(LanguagePack.FromJson(reader.ReadToEnd()));
        }

        return new LanguageCatalog(packs);
    }

    /// <summary>
    /// Returns the pack of a language.
    /// </summary>
    /// <param name="code">The language code.</param>
    /// <returns>The pack, or <see langword="null"/> when the language is unknown.</returns>
    public ILanguagePack? Find(string? code)
        => code is not null && _packs.TryGetValue(code.Trim().ToLowerInvariant(), out var pack) ? pack : null;

    /// <summary>
    /// Chooses the language that matches a culture, for example the language of the operating system.
    /// </summary>
    /// <param name="culture">The culture.</param>
    /// <returns>The language of the culture when the catalog has it, otherwise the default language.</returns>
    public LanguageInfo Detect(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return (Find(culture.TwoLetterISOLanguageName) ?? Default).Language;
    }
}
