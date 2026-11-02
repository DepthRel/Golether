using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;

namespace Golether.Mobile.Controls;

/// <summary>
/// A panel that rises from the bottom of the screen over a dimmed picture. A tap beside it, a swipe down on its grip and
/// the back button close it.
/// </summary>
public sealed class BottomSheet : ContentControl
{
    /// <summary>
    /// How far a finger goes down the grip to close the panel.
    /// </summary>
    private const double SWIPE_DISTANCE = 32;

    /// <summary>
    /// The share of the screen the panel may take.
    /// </summary>
    private const double MAX_HEIGHT_SHARE = 0.85;

    /// <summary>
    /// How long the panel takes to leave; the style moves it for that long.
    /// </summary>
    private static readonly TimeSpan _leaving = TimeSpan.FromMilliseconds(180);

    /// <summary>
    /// The panel itself.
    /// </summary>
    private Control? _panel;

    /// <summary>
    /// Where a finger came down on the grip, or <see langword="null"/>.
    /// </summary>
    private Point? _swipeFrom;

    /// <summary>
    /// Counts the openings, so a closing that was overtaken by a new opening does not hide the panel.
    /// </summary>
    private int _opening;

    /// <summary>
    /// Initializes a new instance of the <see cref="BottomSheet"/> class.
    /// </summary>
    public BottomSheet() => IsVisible = false;

    /// <summary>
    /// Raised when the panel starts to close, whoever closed it.
    /// </summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Gets a value indicating whether the panel is open.
    /// </summary>
    public bool IsOpen { get; private set; }

    /// <summary>
    /// Opens the panel.
    /// </summary>
    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        IsOpen = true;
        _opening++;
        IsVisible = true;

        // The style animates the change of the class, so the panel must first be there without it.
        Dispatcher.UIThread.Post(() => Classes.Set("open", IsOpen), DispatcherPriority.Render);
    }

    /// <summary>
    /// Closes the panel.
    /// </summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        Classes.Set("open", false);
        Closed?.Invoke(this, EventArgs.Empty);

        _ = HideAsync(_opening);
    }

    /// <summary>
    /// Takes the closed panel away when it has left: a panel that only looks gone would still catch the touches. This
    /// is no timer of the dispatcher, those sleep on Android when the screen has nothing to draw.
    /// </summary>
    /// <param name="opening">The opening this closing ends.</param>
    /// <returns>A task that completes when the panel is hidden.</returns>
    private async Task HideAsync(int opening)
    {
        await Task.Delay(_leaving, TimeProvider.System);

        Dispatcher.UIThread.Post(() =>
        {
            if (opening == _opening)
            {
                IsVisible = false;
            }
        });
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _panel = e.NameScope.Find<Control>("PART_Panel");
        var dim = e.NameScope.Find<Control>("PART_Dim");
        var grip = e.NameScope.Find<Control>("PART_Grip");

        if (dim is not null)
        {
            dim.Tapped += (_, _) => Close();
        }

        if (grip is not null)
        {
            grip.PointerPressed += (_, args) => _swipeFrom = args.GetPosition(this);
            grip.PointerReleased += OnGripReleased;
        }

        UpdateMaxHeight(Bounds.Height);
    }

    /// <inheritdoc />
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        UpdateMaxHeight(e.NewSize.Height);
    }

    /// <summary>
    /// Closes the panel when the finger went down the grip.
    /// </summary>
    /// <param name="sender">The grip.</param>
    /// <param name="e">The event.</param>
    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_swipeFrom is { } from && e.GetPosition(this).Y - from.Y >= SWIPE_DISTANCE)
        {
            Close();
        }

        _swipeFrom = null;
    }

    /// <summary>
    /// Leaves a part of the picture above the panel.
    /// </summary>
    /// <param name="height">The height of the screen.</param>
    private void UpdateMaxHeight(double height)
    {
        if (_panel is { } panel && height > 0)
        {
            panel.MaxHeight = height * MAX_HEIGHT_SHARE;
        }
    }
}
