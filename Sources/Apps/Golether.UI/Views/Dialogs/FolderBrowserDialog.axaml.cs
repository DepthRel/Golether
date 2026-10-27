using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Golether.UI.ViewModels.Dialogs;

namespace Golether.UI.Views.Dialogs;

/// <summary>
/// Chooses the folder the host shows; closes with the choice, or with nothing when cancelled.
/// </summary>
public sealed partial class FolderBrowserDialog : Window
{
    /// <summary>
    /// The width an entry takes with its margin.
    /// </summary>
    private const double ENTRY_WIDTH = 140;

    /// <summary>
    /// Initializes a new instance of the <see cref="FolderBrowserDialog"/> class.
    /// </summary>
    public FolderBrowserDialog()
    {
        InitializeComponent();
        EntryRows.SizeChanged += (_, e) => UpdateColumns(e.NewSize.Width);
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        Closed += (_, _) => Model?.Dispose();
    }

    /// <summary>
    /// Gets the view model.
    /// </summary>
    private FolderBrowserViewModel? Model => DataContext as FolderBrowserViewModel;

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (Model is { } model)
        {
            model.Chosen += (_, _) => Close(model.Choice);
            UpdateColumns(EntryRows.Bounds.Width);
        }
    }

    /// <summary>
    /// Fits as many entries into a row as the width takes.
    /// </summary>
    /// <param name="width">The width of the entries area.</param>
    private void UpdateColumns(double width)
    {
        if (Model is { } model && width > 0)
        {
            model.Columns = Math.Max(1, (int)(width / ENTRY_WIDTH));
        }
    }

    /// <summary>
    /// Opens the typed folder on Enter.
    /// </summary>
    /// <param name="sender">The address line.</param>
    /// <param name="e">The event data.</param>
    private void OnPathKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Model is { } model)
        {
            model.GoToPathCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Moves through the folders with the keys of a file manager: Alt+Left and Backspace go back, Alt+Right forward,
    /// Alt+Up to the folder above. Backspace in the address line edits the text.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The event data.</param>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (Model is not { } model)
        {
            return;
        }

        var command = (e.Key, e.KeyModifiers) switch
        {
            (Key.Left, KeyModifiers.Alt) or (Key.BrowserBack, _) => model.BackCommand,
            (Key.Back, KeyModifiers.None) when e.Source is not TextBox => model.BackCommand,
            (Key.Right, KeyModifiers.Alt) or (Key.BrowserForward, _) => model.ForwardCommand,
            (Key.Up, KeyModifiers.Alt) => model.UpCommand,
            _ => null,
        };

        if (command is not null)
        {
            command.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Moves through the folders with the side buttons of the mouse: the back and the forward one.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The event data.</param>
    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Model is not { } model)
        {
            return;
        }

        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsXButton1Pressed)
        {
            model.BackCommand.Execute(null);
            e.Handled = true;
        }
        else if (properties.IsXButton2Pressed)
        {
            model.ForwardCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Closes without a choice.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event data.</param>
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
