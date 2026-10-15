using Avalonia;
using Avalonia.Headless;

namespace Golether.Web.Server.UI.Tests;

/// <summary>
/// The monitor application on the headless platform, drawn with Skia so frames can be saved.
/// </summary>
public static class TestApp
{
    /// <summary>
    /// Builds the application.
    /// </summary>
    /// <returns>The builder.</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
