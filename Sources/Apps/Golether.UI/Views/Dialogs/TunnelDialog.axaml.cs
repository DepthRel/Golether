using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Golether.UI.Views.Dialogs;

/// <summary>
/// Negotiates AmneziaWG tunnels.
/// </summary>
public sealed partial class TunnelDialog : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TunnelDialog"/> class.
    /// </summary>
    public TunnelDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Closes the dialog.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event data.</param>
    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
