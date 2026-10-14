using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Golether.Web.Server.UI.Views;

/// <summary>
/// Asks the user to confirm an action that is hard to undo. Enter and Escape both answer "Cancel": only a deliberate
/// click on the action goes ahead.
/// </summary>
public sealed partial class ConfirmDialog : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConfirmDialog"/> class (for the designer).
    /// </summary>
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfirmDialog"/> class.
    /// </summary>
    /// <param name="title">The question.</param>
    /// <param name="message">What the action does.</param>
    /// <param name="acceptText">The caption of the button that goes ahead.</param>
    public ConfirmDialog(string title, string message, string acceptText)
        : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        AcceptButton.Content = acceptText;
    }

    /// <summary>
    /// Goes ahead.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event data.</param>
    private void OnAccept(object? sender, RoutedEventArgs e) => Close(true);

    /// <summary>
    /// Gives up.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event data.</param>
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
