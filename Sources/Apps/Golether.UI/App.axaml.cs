using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
            try
            {
                _services = AppServices.Create();
            }
            catch (Exception ex)
            {
                desktop.MainWindow = new MessageDialog("Golether не запустился", ex.Message);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            var window = new MainWindow();
            _services.SetDialogs(new AvaloniaDialogService(window));
            var viewModel = _services.CreateMainViewModel();
            window.Initialize(viewModel, _services.Player, _services.Conference);
            var fileToShow = desktop.Args?.FirstOrDefault(a => !a.StartsWith('-') && File.Exists(a));
            window.Opened += async (_, _) =>
            {
                await viewModel.LoadAsync();
                if (fileToShow is not null)
                {
                    // "Open with Golether": host a session and show the file right away.
                    await viewModel.HostFileAsync(Path.GetFullPath(fileToShow));
                }
            };
            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.Exit += (_, _) => _services.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
