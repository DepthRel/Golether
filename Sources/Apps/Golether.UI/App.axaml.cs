using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Golether.Localization;
using Golether.UI.Services;
using Golether.UI.Views;
using Golether.UI.Views.Dialogs;

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

    /// <summary>
    /// Takes links the system hands over while the application runs, or <see langword="null"/> until the main
    /// window is ready.
    /// </summary>
    private Action<string[]>? _external;

    /// <summary>
    /// The links the system handed over before the main window was ready. Used on the UI thread only.
    /// </summary>
    private readonly List<string[]> _waiting = [];

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
            // macOS does not put a clicked link on the command line: it sends an event to the running application.
            if (this.TryGetFeature<IActivatableLifetime>() is { } activatable)
            {
                activatable.Activated += (_, e) =>
                {
                    if (e is ProtocolActivatedEventArgs { Uri: { } uri })
                    {
                        string[] link = [uri.OriginalString];
                        if (_external is { } external)
                        {
                            external(link);
                        }
                        else
                        {
                            _waiting.Add(link);
                        }
                    }
                };
            }

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
            var arguments = desktop.Args ?? [];
            window.Opened += async (_, _) =>
            {
                // The window is drawn by now: the splash goes away without a gap.
                splash.Close();
                try
                {
                    // Started by an invitation link: the window says so from its first moment, before the settings
                    // are loaded and the question is asked, instead of showing the start screen as if nothing came.
                    viewModel.IsOpeningInvite = arguments.Any(Golether.Security.Invites.Invite.LooksLikeLink);
                    await viewModel.LoadAsync();

                    // What this start was given (an invitation link, a video file), then whatever later starts and
                    // the system hand over while the application runs.
                    await viewModel.OpenExternalAsync(arguments);
                    _external = later => Dispatcher.UIThread.Post(() => _ = OpenExternalAsync(viewModel, later, log));
                    Program.Instance?.SetHandler(_external);
                    foreach (var waiting in _waiting)
                    {
                        _external(waiting);
                    }

                    _waiting.Clear();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // The handler returns nothing, so an exception here would end the application. The window is
                    // open and works with the settings it has; what failed goes to the log.
                    viewModel.IsOpeningInvite = false;
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
    /// Gives arguments that arrived from outside to the main window; a failure goes to the log instead of ending
    /// the application.
    /// </summary>
    /// <param name="viewModel">The main view model.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="log">The logger.</param>
    /// <returns>A task that completes when the arguments were dealt with.</returns>
    private static async Task OpenExternalAsync(ViewModels.MainWindowViewModel viewModel, string[] arguments, ILogger log)
    {
        try
        {
            await viewModel.OpenExternalAsync(arguments);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.LogError(ex, "What the system handed over could not be opened");
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
