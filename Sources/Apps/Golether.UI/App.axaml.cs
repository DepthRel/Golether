using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
            var failure = new MessageDialog(Texts.Get("Splash.StartFailed"), ex.Message);
            desktop.MainWindow = failure;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            failure.Show();
            splash.Close();
            return;
        }

        _services = services;
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
            await viewModel.LoadAsync();
            if (fileToShow is not null)
            {
                // "Open with Golether": host a session and show the file right away.
                await viewModel.HostFileAsync(Path.GetFullPath(fileToShow));
            }
        };
        desktop.MainWindow = window;
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        desktop.Exit += (_, _) => services.DisposeAsync().AsTask().GetAwaiter().GetResult();
        window.Show();
    }
}
