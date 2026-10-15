using System.Text.Json;

namespace Golether.Localization;

/// <summary>
/// A language pack that keeps its texts in a dictionary, usually read from a JSON file.
/// </summary>
public sealed class LanguagePack : ILanguagePack
{
    /// <summary>
    /// The key holding the code of the language.
    /// </summary>
    public const string CODE_KEY = "Language.Code";

    /// <summary>
    /// The key holding the name of the language in that language.
    /// </summary>
    public const string NAME_KEY = "Language.Name";

    /// <summary>
    /// The texts by key.
    /// </summary>
    private readonly IReadOnlyDictionary<string, string> _texts;

    /// <summary>
    /// Initializes a new instance of the <see cref="LanguagePack"/> class.
    /// </summary>
    /// <param name="language">The language.</param>
    /// <param name="texts">The texts by key.</param>
    public LanguagePack(LanguageInfo language, IReadOnlyDictionary<string, string> texts)
    {
        Language = language ?? throw new ArgumentNullException(nameof(language));
        _texts = texts ?? throw new ArgumentNullException(nameof(texts));
    }

    /// <inheritdoc />
    public LanguageInfo Language { get; }

    /// <inheritdoc />
    public IReadOnlyCollection<string> Keys => _texts.Keys.ToArray();

    /// <inheritdoc />
    public bool TryGet(string key, out string text)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _texts.TryGetValue(key, out text!);
    }

    /// <summary>
    /// Reads a pack from a JSON object of <c>key: text</c> pairs; the pairs <see cref="CODE_KEY"/> and
    /// <see cref="NAME_KEY"/> describe the language.
    /// </summary>
    /// <param name="json">The JSON document.</param>
    /// <returns>The pack.</returns>
    /// <exception cref="InvalidDataException">The document is not a flat object of strings or does not name its language.</exception>
    public static LanguagePack FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        Dictionary<string, string>? texts;
        try
        {
            texts = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("A language file must be a JSON object of text values: " + ex.Message, ex);
        }

        if (texts is null || !texts.TryGetValue(CODE_KEY, out var code) || string.IsNullOrWhiteSpace(code) ||
            !texts.TryGetValue(NAME_KEY, out var name) || string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidDataException($"A language file must define \"{CODE_KEY}\" and \"{NAME_KEY}\".");
        }

        return new LanguagePack(new LanguageInfo(code.Trim().ToLowerInvariant(), name.Trim()), texts);
    }
}
