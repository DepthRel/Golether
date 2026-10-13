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
    /// Initializes a new instance of the <see cref="MessageDialog"/> class that asks a question: the dialog closes
    /// with <see langword="true"/> when the user agrees.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="message">The question.</param>
    /// <param name="acceptText">The text of the button that agrees.</param>
    public MessageDialog(string title, string message, string acceptText)
        : this(title, message)
    {
        OkButton.Content = acceptText;
        CancelButton.IsVisible = true;
    }

    /// <summary>
    /// Closes the dialog; a question is answered with yes.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event data.</param>
    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    /// <summary>
    /// Closes the dialog, answering a question with no.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event data.</param>
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
