using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Golether.Localization;
using Golether.UI.Services;
using Golether.UI.Views;
using Golether.UI.Views.Dialogs;
using Microsoft.Extensions.Logging;

namespace Golether.UI;

/// <summary>
/// The Avalonia application.
/// </summary>
public sealed class App : Application
{
    /// <summary>
    /// The application services.
    /// </summary>
    private AppServices? _services;

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Nothing is the main window yet: the splash must be able to close without ending the application.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // The stored language is not known before the database is ready; until then the splash and a start-up
            // failure speak the language of the operating system.
            var localizer = Texts.Localizer;
            localizer.SetLanguage(localizer.Detect(System.Globalization.CultureInfo.CurrentUICulture).Code);
            var splash = new SplashWindow();
            splash.Show();
            _ = StartAsync(desktop, splash);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Prepares the application behind the splash and opens the main window when everything is ready. The database,
    /// the migrations and the device key take a few seconds on the first launch, so they run off the UI thread and
    /// the splash keeps drawing itself.
    /// </summary>
    /// <param name="desktop">The application lifetime.</param>
    /// <param name="splash">The splash window.</param>
    /// <returns>A task that completes when the main window is shown.</returns>
    private async Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop, SplashWindow splash)
    {
        AppServices services;
        try
        {
            splash.ShowStep(Texts.Get("Splash.PreparingData"));
            services = await Task.Run(AppServices.Create);
        }
        catch (Exception ex)
        {
            ShowStartFailure(desktop, splash, ex);
            return;
        }

        _services = services;
        var log = services.LoggerFactory.CreateLogger<App>();
        LogCrashes(log);
        try
        {
            splash.ShowStep(Texts.Get("Splash.OpeningWindow"));
            var window = new MainWindow();
            services.SetDialogs(new AvaloniaDialogService(window));
            var viewModel = services.CreateMainViewModel();
            window.Initialize(viewModel, services.Player, services.Conference);
            var fileToShow = desktop.Args?.FirstOrDefault(a => !a.StartsWith('-') && File.Exists(a));
            window.Opened += async (_, _) =>
            {
                // The window is drawn by now: the splash goes away without a gap.
                splash.Close();
                try
                {
                    await viewModel.LoadAsync();
                    if (fileToShow is not null)
                    {
                        // "Open with Golether": host a session and show the file right away.
                        await viewModel.HostFileAsync(Path.GetFullPath(fileToShow));
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // The handler returns nothing, so an exception here would end the application. The window is
                    // open and works with the settings it has; what failed goes to the log.
                    log.LogError(ex, "The stored settings were not applied");
                }
            };
            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.Exit += (_, _) => services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            window.Show();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Nobody waits for this method: without the message the splash would stay on the screen for ever.
            log.LogCritical("The main window could not be opened: {Exception}", ex);
            ShowStartFailure(desktop, splash, ex);
        }
    }

    /// <summary>
    /// Tells the user that the application could not start and lets it end when the message is closed.
    /// </summary>
    /// <param name="desktop">The application lifetime.</param>
    /// <param name="splash">The splash window.</param>
    /// <param name="error">What went wrong.</param>
    private static void ShowStartFailure(IClassicDesktopStyleApplicationLifetime desktop, SplashWindow splash, Exception error)
    {
        var failure = new MessageDialog(Texts.Get("Splash.StartFailed"), error.Message);
        desktop.MainWindow = failure;
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        failure.Show();
        splash.Close();
    }

    /// <summary>
    /// Writes what would otherwise vanish into the log: the exception the application stops on, and the failures of
    /// background tasks nobody waited for. The log is what a diagnostic report carries, so a problem can be looked
    /// at afterwards.
    /// </summary>
    /// <param name="log">The logger.</param>
    private static void LogCrashes(ILogger log)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            log.LogCritical("The application stopped on an unhandled exception: {Exception}", e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.LogWarning("A background task failed: {Exception}", e.Exception);
            e.SetObserved();
        };
    }
}
