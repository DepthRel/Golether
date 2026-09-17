using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Golether.UI.Views.Dialogs;

/// <summary>
/// A message box.
/// </summary>
public sealed partial class MessageDialog : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MessageDialog"/> class for the designer.
    /// </summary>
    public MessageDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageDialog"/> class.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="message">The message.</param>
    public MessageDialog(string title, string message)
        : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
    }

    /// <summary>
    /// Closes the dialog.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event data.</param>
    private void OnOk(object? sender, RoutedEventArgs e) => Close();
}
