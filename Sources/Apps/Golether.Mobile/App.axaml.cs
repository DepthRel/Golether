using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Golether.Client.Services;
using Golether.Mobile.Services;
using Golether.Mobile.ViewModels;
using Golether.Mobile.Views;

namespace Golether.Mobile;

/// <summary>
/// The mobile client: a single view on the whole screen, without windows.
/// </summary>
public sealed class App : Application
{
    /// <summary>
    /// The model of the screen, once it exists.
    /// </summary>
    private static MainViewModel? _model;

    /// <summary>
    /// An invitation that came before the model was ready.
    /// </summary>
    private static string? _pendingInvite;

    /// <summary>
    /// Gets or sets the system the client runs on; the application sets it before Avalonia starts.
    /// </summary>
    public static IMobilePlatform? Platform { get; set; }

    /// <summary>
    /// Hands over an invitation that came from a link.
    /// </summary>
    /// <param name="link">The key or the link of the invitation.</param>
    public static void OpenInvite(string link)
    {
        if (_model is { } model)
        {
            Dispatcher.UIThread.Post(() => model.OpenInvite(link));
        }
        else
        {
            _pendingInvite = link;
        }
    }

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime lifetime)
        {
            var platform = Platform ?? throw new InvalidOperationException("The platform of the client is not set.");
            var view = new MainView();
            var model = new MainViewModel(
                platform,
                new AvaloniaUiDispatcher(),
                () => TopLevel.GetTopLevel(view)?.StorageProvider,
                async features => await MobileServices.CreateAsync(platform, features));
            _model = model;
            view.DataContext = model;
            lifetime.MainView = view;

            if (_pendingInvite is { } invite)
            {
                model.OpenInvite(invite);
            }

            _ = model.StartAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Runs work on the UI thread of Avalonia.
    /// </summary>
    private sealed class AvaloniaUiDispatcher : IUiDispatcher
    {
        /// <inheritdoc />
        public void Post(Action action) => Dispatcher.UIThread.Post(action);
    }
}
