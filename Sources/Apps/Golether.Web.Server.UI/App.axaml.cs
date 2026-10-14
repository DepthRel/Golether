using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Golether.Web.Server.UI.Services;
using Golether.Web.Server.UI.ViewModels;
using Golether.Web.Server.UI.Views;

namespace Golether.Web.Server.UI;

/// <summary>
/// The monitor application: one window.
/// </summary>
public sealed class App : Application
{
    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var paths = ServerPaths.Find(AppContext.BaseDirectory);
            var runner = new ProcessRunner();
            var service = ServiceManagers.ForThisSystem(runner);
            var settings = new ServerSettingsStore(paths.SettingsFile, service);
            var window = new MainWindow();
            var dialogs = new AvaloniaDialogs(window);
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            window.DataContext = new MainViewModel(
                new ServerApi(http, () => settings.StatusUrl()),
                service,
                settings,
                dialogs,
                TimeProvider.System,
                MonitorPreferences.NextTo(paths));
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => http.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
