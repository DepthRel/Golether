using System.Runtime.CompilerServices;
using Golether.Localization;

namespace Golether.Tests;

/// <summary>
/// Puts the texts of the code under test into Russian for the whole test run. The expectations of the tests were
/// written against the Russian texts, which are the reference of the Russian language pack; tests of other languages
/// build their own localizer and never touch <see cref="Texts.Localizer"/>.
/// </summary>
internal static class UseRussianTexts
{
    /// <summary>
    /// Runs once when the test assembly loads.
    /// </summary>
    [ModuleInitializer]
    internal static void Apply() => Texts.Localizer = new Localizer(LanguageCatalog.LoadEmbedded(), "ru");
}
