using System.Globalization;
using Golether.Localization;

namespace Golether.Localization.Tests;

/// <summary>
/// Tests of the language packs, the catalog and the localizer that picks texts from them.
/// </summary>
public sealed class LocalizerTests
{
    /// <summary>
    /// Builds a small pack.
    /// </summary>
    /// <param name="code">The language code.</param>
    /// <param name="name">The name of the language.</param>
    /// <param name="texts">The texts.</param>
    /// <returns>The pack.</returns>
    private static LanguagePack Pack(string code, string name, params (string Key, string Text)[] texts)
        => new(new LanguageInfo(code, name), texts.ToDictionary(t => t.Key, t => t.Text));

    /// <summary>
    /// Builds a catalog of English, Russian and a third language with a gap.
    /// </summary>
    /// <returns>The catalog.</returns>
    private static LanguageCatalog Catalog() => new(
    [
        Pack("en", "English", ("Hello", "Hello"), ("Only.English", "Only in English"), ("Count", "{0} items, {1:0.0}")),
        Pack("ru", "Русский", ("Hello", "Привет"), ("Count", "{0} шт., {1:0.0}")),
        Pack("de", "Deutsch", ("Hello", "Hallo")),
    ]);

    /// <summary>
    /// A language file is a flat JSON object that names its own language.
    /// </summary>
    [Fact]
    public void LanguagePack_ReadsJson()
    {
        var pack = LanguagePack.FromJson("""{"Language.Code": "FR", "Language.Name": "Français", "Hello": "Bonjour \"toi\""}""");

        Assert.Equal("fr", pack.Language.Code);
        Assert.Equal("Français", pack.Language.NativeName);
        Assert.True(pack.TryGet("Hello", out var text));
        Assert.Equal("Bonjour \"toi\"", text);
        Assert.False(pack.TryGet("Missing", out _));
    }

    /// <summary>
    /// A language file without its code or name, and a file that is not a flat object of strings, are refused with
    /// a clear message.
    /// </summary>
    [Theory]
    [InlineData("""{"Hello": "x"}""")]
    [InlineData("""{"Language.Code": "xx"}""")]
    [InlineData("""{"Language.Code": " ", "Language.Name": "X"}""")]
    [InlineData("""{"Language.Code": "xx", "Language.Name": "X", "Nested": {"a": "b"}}""")]
    [InlineData("[1, 2]")]
    [InlineData("not json")]
    public void LanguagePack_RefusesBrokenFiles(string json)
        => Assert.Throws<InvalidDataException>(() => LanguagePack.FromJson(json));

    /// <summary>
    /// The catalog needs the default language and refuses two packs of one language.
    /// </summary>
    [Fact]
    public void Catalog_ValidatesItsPacks()
    {
        Assert.Throws<ArgumentException>(() => new LanguageCatalog([Pack("ru", "Русский")]));
        Assert.Throws<ArgumentException>(() => new LanguageCatalog([Pack("en", "English"), Pack("en", "English 2")]));
    }

    /// <summary>
    /// The application carries English and Russian, English is the default, and the languages are offered by name.
    /// </summary>
    [Fact]
    public void EmbeddedCatalog_HasEnglishAndRussian()
    {
        var catalog = LanguageCatalog.LoadEmbedded();

        Assert.Equal(["en", "ru"], catalog.Languages.Select(l => l.Code));
        Assert.Equal(["English", "Русский"], catalog.Languages.Select(l => l.NativeName));
        Assert.Equal("en", catalog.Default.Language.Code);
    }

    /// <summary>
    /// The operating system decides the language of a first start: Russian gets Russian, everything else the default
    /// language, whatever the region is.
    /// </summary>
    [Theory]
    [InlineData("ru-RU", "ru")]
    [InlineData("ru", "ru")]
    [InlineData("ru-KZ", "ru")]
    [InlineData("en-US", "en")]
    [InlineData("en-GB", "en")]
    [InlineData("de-DE", "en")]
    [InlineData("ja-JP", "en")]
    [InlineData("", "en")]
    public void Detect_ChoosesRussianOrTheDefault(string culture, string expected)
        => Assert.Equal(expected, LanguageCatalog.LoadEmbedded().Detect(CultureInfo.GetCultureInfo(culture)).Code);

    /// <summary>
    /// A language is chosen by looking it up, and a language with a pack is found whatever the case of its code.
    /// </summary>
    [Fact]
    public void Detect_FindsAnyLanguageThatHasAPack()
    {
        Assert.Equal("de", Catalog().Detect(CultureInfo.GetCultureInfo("de-AT")).Code);
        Assert.Equal("ru", Catalog().Find(" RU ")?.Language.Code);
        Assert.Null(Catalog().Find("xx"));
        Assert.Null(Catalog().Find(null));
    }

    /// <summary>
    /// A text comes from the current language; the language switches at run time and tells about it.
    /// </summary>
    [Fact]
    public void Localizer_SwitchesLanguages()
    {
        var localizer = new Localizer(Catalog());
        var changes = 0;
        localizer.LanguageChanged += (_, _) => changes++;

        Assert.Equal("Hello", localizer.Get("Hello"));
        localizer.SetLanguage("ru");
        Assert.Equal("Привет", localizer.Get("Hello"));
        Assert.Equal("ru", localizer.Current.Code);
        localizer.SetLanguage("ru");

        Assert.Equal(1, changes);
        Assert.Throws<ArgumentException>(() => localizer.SetLanguage("xx"));
        Assert.Equal("ru", localizer.Current.Code);
    }

    /// <summary>
    /// A text a language lacks comes from the default language; a text nobody has shows its key.
    /// </summary>
    [Fact]
    public void Localizer_FallsBackToTheDefaultLanguage()
    {
        var localizer = new Localizer(Catalog(), "de");

        Assert.Equal("Hallo", localizer.Get("Hello"));
        Assert.Equal("Only in English", localizer.Get("Only.English"));
        Assert.Equal("No.Such.Key", localizer.Get("No.Such.Key"));
    }

    /// <summary>
    /// Values are put in by the culture of the language, so the decimal mark follows it.
    /// </summary>
    [Fact]
    public void Localizer_FormatsByTheCultureOfTheLanguage()
    {
        var localizer = new Localizer(Catalog(), "ru");
        Assert.Equal("3 шт., 1,5", localizer.Format("Count", 3, 1.5));

        localizer.SetLanguage("en");
        Assert.Equal("3 items, 1.5", localizer.Format("Count", 3, 1.5));
    }

    /// <summary>
    /// The ambient texts of the application follow the localizer that is put in place of the default one.
    /// </summary>
    [Fact]
    public void Texts_FollowsTheReplacedLocalizer()
    {
        var before = Texts.Localizer;
        try
        {
            Texts.Localizer = new Localizer(Catalog(), "ru");
            Assert.Equal("Привет", Texts.Get("Hello"));
            Assert.Equal("2 шт., 0,5", Texts.Format("Count", 2, 0.5));
        }
        finally
        {
            Texts.Localizer = before;
        }
    }
}
