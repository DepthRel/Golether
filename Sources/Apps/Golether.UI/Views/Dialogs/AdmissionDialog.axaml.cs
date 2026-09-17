using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Golether.UI.Views.Dialogs;

/// <summary>
/// Asks the host to admit a participant.
/// </summary>
public sealed partial class AdmissionDialog : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AdmissionDialog"/> class.
    /// </summary>
    public AdmissionDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Admits the participant.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event data.</param>
    private void OnApprove(object? sender, RoutedEventArgs e) => Close(true);

    /// <summary>
    /// Rejects the participant.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event data.</param>
    private void OnReject(object? sender, RoutedEventArgs e) => Close(false);
}
