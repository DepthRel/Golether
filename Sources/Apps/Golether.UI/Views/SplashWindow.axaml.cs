using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Golether.UI.Views;

/// <summary>
/// The window shown while the application prepares itself, so the user never looks at an empty frame.
/// </summary>
public sealed partial class SplashWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SplashWindow"/> class.
    /// </summary>
    public SplashWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Shows what the application is doing now.
    /// </summary>
    /// <param name="text">The step.</param>
    public void ShowStep(string text)
        => Dispatcher.UIThread.Post(() => this.FindControl<TextBlock>("Status")!.Text = text);
}
