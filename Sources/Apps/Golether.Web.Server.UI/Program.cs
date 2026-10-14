using System.Globalization;
using Avalonia;
using Golether.Localization;

namespace Golether.Web.Server.UI;

/// <summary>
/// The entry point of the monitor.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Starts the monitor in the language the user chose in it before, or else in the language of the system; a
    /// language Golether does not speak falls back to English.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The exit code.</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        var catalog = LanguageCatalog.LoadEmbedded();
        var chosen = Services.MonitorPreferences.NextTo(Services.ServerPaths.Find(AppContext.BaseDirectory)).LoadLanguage();
        var code = chosen is not null && catalog.Find(chosen) is not null ? chosen : catalog.Detect(CultureInfo.CurrentUICulture).Code;
        Texts.Localizer = new Localizer(catalog, code);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Builds the Avalonia application; also used by the designer.
    /// </summary>
    /// <returns>The application builder.</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
}
