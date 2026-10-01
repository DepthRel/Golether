using Avalonia;
using Avalonia.Headless;

namespace Golether.UI.Headless.Tests;

/// <summary>
/// Builds the application for the headless platform.
/// </summary>
public static class TestApp
{
    /// <summary>
    /// Configures Avalonia without a screen.
    /// </summary>
    /// <returns>The application builder.</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
