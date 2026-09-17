using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Golether.UI.Views.Dialogs;

/// <summary>
/// Shows an invitation link.
/// </summary>
public sealed partial class InviteDialog : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InviteDialog"/> class.
    /// </summary>
    public InviteDialog()
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
