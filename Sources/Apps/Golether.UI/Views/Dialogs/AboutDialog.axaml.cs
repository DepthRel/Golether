using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Golether.UI.Views.Dialogs;

/// <summary>
/// The window about the application.
/// </summary>
public sealed partial class AboutDialog : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AboutDialog"/> class.
    /// </summary>
    public AboutDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Closes the window.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event data.</param>
    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
