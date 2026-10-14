using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Golether.Web.Server.UI.ViewModels;

namespace Golether.Web.Server.UI.Views;

/// <summary>
/// The window of the monitor. It asks for news every second while it is open.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>
    /// Asks for news.
    /// </summary>
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await RefreshAsync();
        Opened += async (_, _) =>
        {
            await RefreshAsync();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>
    /// Asks the model for news; the model ignores a refresh while the previous one runs.
    /// </summary>
    /// <returns>A task that completes when the view is up to date.</returns>
    private async Task RefreshAsync()
    {
        if (DataContext is MainViewModel model)
        {
            await model.RefreshAsync();
        }
    }

    /// <summary>
    /// Copies the selected rows as lines of the exported journal.
    /// </summary>
    /// <param name="sender">The menu entry.</param>
    /// <param name="e">The event data.</param>
    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        var lines = Journal.SelectedItems?.OfType<JournalRowViewModel>().Select(row => row.ToLine()).ToArray() ?? [];
        if (lines.Length > 0 && Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(string.Join(Environment.NewLine, lines));
        }
    }
}
