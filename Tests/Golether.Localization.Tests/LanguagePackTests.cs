using System.Text.RegularExpressions;
using Golether.Localization;

namespace Golether.Localization.Tests;

/// <summary>
/// Checks every language the application carries: the same texts as the default language, the same placeholders,
/// and every key the code and the screens ask for.
/// </summary>
public sealed partial class LanguagePackTests
{
    /// <summary>
    /// The pattern of a <c>{0}</c> placeholder, with an optional format.
    /// </summary>
    [GeneratedRegex(@"\{(\d+)(?::[^}]*)?\}")]
    private static partial Regex Placeholder();

    /// <summary>
    /// The pattern of a key asked for from code: <c>Texts.Get("Key")</c> and <c>Texts.Format("Key", …)</c>. A prefix
    /// that ends with a dot (<c>"Session.Reject." + reason</c>) is a family of keys chosen by an enumeration; the
    /// tests of the projects that own the enumerations check that every value has its text.
    /// </summary>
    [GeneratedRegex(@"Texts\.(?:Get|Format)\(\s*""(?<key>[^""]*[^"".])""")]
    private static partial Regex CodeKey();

    /// <summary>
    /// The pattern of a key asked for from XAML: <c>{loc:Loc Key}</c> and <c>{loc:Loc Key=Key, …}</c>.
    /// </summary>
    [GeneratedRegex(@"\{loc:Loc\s+(?:Key=)?(?<key>[\w\.]+)")]
    private static partial Regex XamlKey();

    /// <summary>
    /// Returns the folder of the repository.
    /// </summary>
    /// <returns>The folder that holds <c>Golether.slnx</c>.</returns>
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Golether.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Golether.slnx was not found above " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// Returns the files under <c>Sources</c> with an extension, without build output.
    /// </summary>
    /// <param name="extension">The extension with the dot.</param>
    /// <returns>The paths.</returns>
    private static IEnumerable<string> SourceFiles(string extension)
        => Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "Sources"), "*" + extension, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>
    /// Returns the texts of a pack by key.
    /// </summary>
    /// <param name="pack">The pack.</param>
    /// <returns>The texts.</returns>
    private static Dictionary<string, string> TextsOf(ILanguagePack pack)
        => pack.Keys.ToDictionary(k => k, k => pack.TryGet(k, out var text) ? text : string.Empty);

    /// <summary>
    /// Returns the packs the application carries, with the default language first.
    /// </summary>
    /// <returns>The packs.</returns>
    public static TheoryData<string> Languages()
    {
        var data = new TheoryData<string>();
        foreach (var language in LanguageCatalog.LoadEmbedded().Languages)
        {
            data.Add(language.Code);
        }

        return data;
    }

    /// <summary>
    /// A language has exactly the texts of the default language: nothing is missing, so nothing falls back to another
    /// language on the screen, and nothing is left over from a text that was removed.
    /// </summary>
    /// <param name="code">The language.</param>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Language_HasTheKeysOfTheDefaultLanguage(string code)
    {
        var catalog = LanguageCatalog.LoadEmbedded();
        var reference = TextsOf(catalog.Default);
        var texts = TextsOf(catalog.Find(code)!);

        Assert.Empty(reference.Keys.Except(texts.Keys).Order());
        Assert.Empty(texts.Keys.Except(reference.Keys).Order());
        Assert.Empty(texts.Where(t => string.IsNullOrWhiteSpace(t.Value)).Select(t => t.Key));
    }

    /// <summary>
    /// A translation puts in the same values as the original: a text with <c>{0}</c> and <c>{1}</c> in one language
    /// cannot lose one of them in another.
    /// </summary>
    /// <param name="code">The language.</param>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Language_KeepsThePlaceholdersOfTheDefaultLanguage(string code)
    {
        var catalog = LanguageCatalog.LoadEmbedded();
        var reference = TextsOf(catalog.Default);
        var texts = TextsOf(catalog.Find(code)!);
        string Indexes(string text) => string.Join(',', Placeholder().Matches(text).Select(m => m.Groups[1].Value).Distinct().Order());

        var mismatches = reference
            .Where(r => texts.TryGetValue(r.Key, out var translated) && Indexes(r.Value) != Indexes(translated))
            .Select(r => $"{r.Key}: {Indexes(r.Value)} vs {Indexes(texts[r.Key])}");

        Assert.Empty(mismatches);
    }

    /// <summary>
    /// Every language names itself in its own language, once.
    /// </summary>
    [Fact]
    public void Languages_HaveDistinctNames()
    {
        var languages = LanguageCatalog.LoadEmbedded().Languages;

        Assert.Equal(languages.Count, languages.Select(l => l.Code).Distinct().Count());
        Assert.Equal(languages.Count, languages.Select(l => l.NativeName).Distinct().Count());
    }

    /// <summary>
    /// Every key the code asks for exists, so a typo is caught here and not as a raw key on the screen.
    /// </summary>
    [Fact]
    public void CodeAsksOnlyForKeysThatExist()
    {
        var known = LanguageCatalog.LoadEmbedded().Default.Keys.ToHashSet();
        var asked = SourceFiles(".cs")
            .SelectMany(file => CodeKey().Matches(File.ReadAllText(file)).Select(m => (File: file, Key: m.Groups["key"].Value)))
            .ToArray();

        Assert.NotEmpty(asked);
        Assert.Empty(asked.Where(a => !known.Contains(a.Key)).Select(a => $"{Path.GetFileName(a.File)}: {a.Key}"));
    }

    /// <summary>
    /// Every key the screens ask for exists.
    /// </summary>
    [Fact]
    public void ScreensAskOnlyForKeysThatExist()
    {
        var known = LanguageCatalog.LoadEmbedded().Default.Keys.ToHashSet();
        var asked = SourceFiles(".axaml")
            .SelectMany(file => XamlKey().Matches(File.ReadAllText(file)).Select(m => (File: file, Key: m.Groups["key"].Value)))
            .ToArray();

        Assert.NotEmpty(asked);
        Assert.Empty(asked.Where(a => !known.Contains(a.Key)).Select(a => $"{Path.GetFileName(a.File)}: {a.Key}"));
    }

    /// <summary>
    /// The screens and the messages carry no text of a language in their code: whatever the user reads comes from a
    /// language pack. Comments are not texts, and neither are the words of the verification code, which both sides
    /// of a session must show the same in every interface language.
    /// </summary>
    [Fact]
    public void NoRussianTextIsLeftInTheCode()
    {
        var cyrillic = new Regex("[Ѐ-ӿ]");
        var exceptions = new[] { "VerificationCode.cs" };
        var leftovers = SourceFiles(".cs").Concat(SourceFiles(".axaml"))
            .Where(f => !exceptions.Contains(Path.GetFileName(f)))
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (File: file, Line: index + 1, Text: line))
                .Where(l => cyrillic.IsMatch(l.Text) && !IsComment(l.Text)))
            .Select(l => $"{Path.GetFileName(l.File)}:{l.Line}: {l.Text.Trim()}");

        Assert.Empty(leftovers);
    }

    /// <summary>
    /// Tells whether a line of code or markup is only a comment.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns><see langword="true"/> for <c>//</c>, <c>///</c> and the lines of an XML comment.</returns>
    private static bool IsComment(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("<!--", StringComparison.Ordinal)
            || trimmed.StartsWith("-->", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal)
            || !trimmed.Contains('<') && !trimmed.Contains('"') && !trimmed.Contains('=') && !trimmed.Contains(';');
    }
}
