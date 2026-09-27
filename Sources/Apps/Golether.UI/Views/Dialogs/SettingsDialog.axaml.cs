using Avalonia.Controls;
using Avalonia.Interactivity;
using Golether.UI.ViewModels.Dialogs;

namespace Golether.UI.Views.Dialogs;

/// <summary>
/// The settings window: language, avatar, camera and microphone, and AmneziaWG tunnels. The choices take effect only
/// when the user applies them.
/// </summary>
public sealed partial class SettingsDialog : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsDialog"/> class.
    /// </summary>
    public SettingsDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Applies the chosen settings and closes the window.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event data.</param>
    private async void OnApply(object? sender, RoutedEventArgs e)
    {
        // The buttons are disabled while the settings are handed over, so a second click cannot apply them twice.
        if (sender is Button button)
        {
            button.IsEnabled = false;
        }

        if (DataContext is SettingsViewModel model)
        {
            await model.ApplyAsync();
        }

        Close();
    }

    /// <summary>
    /// Closes the window, leaving every setting as it was.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event data.</param>
    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
