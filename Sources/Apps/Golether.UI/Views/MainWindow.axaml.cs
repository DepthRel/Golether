using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Golether.Media.Player;
using Golether.UI.Services;
using Golether.UI.ViewModels;

namespace Golether.UI.Views;

/// <summary>
/// The main window.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>
    /// Refreshes the session view.
    /// </summary>
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    /// <summary>
    /// The player host.
    /// </summary>
    private PlayerHost? _player;

    /// <summary>
    /// Draws camera frames into the participant tiles.
    /// </summary>
    private Controls.CameraRenderer? _cameras;

    /// <summary>
    /// Moves the timeline smoothly.
    /// </summary>
    private readonly Controls.TimelineAnimator _timeline;

    /// <summary>
    /// How long a click on the video waits for a second click before it toggles playback.
    /// </summary>
    private static readonly TimeSpan DoubleClickWindow = TimeSpan.FromMilliseconds(320);

    /// <summary>
    /// Toggles playback after a single click on the video.
    /// </summary>
    private readonly DispatcherTimer _clickTimer = new() { Interval = DoubleClickWindow };

    /// <summary>
    /// Closes the volume popup shortly after the pointer left the speaker and the popup.
    /// </summary>
    private readonly DispatcherTimer _volumeCloseTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };

    /// <summary>
    /// The time of the last double click on the video; clicks right after it belong to it.
    /// </summary>
    private DateTimeOffset _lastDoubleClick;

    /// <summary>
    /// Whether the window may close (the session has been left).
    /// </summary>
    private bool _closeConfirmed;

    /// <summary>
    /// The window state before full screen.
    /// </summary>
    private WindowState _stateBeforeFullScreen = WindowState.Normal;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        _timeline = new Controls.TimelineAnimator(Timeline);
        Timeline.AddHandler(PointerPressedEvent, OnTimelinePressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        Timeline.AddHandler(PointerReleasedEvent, OnTimelineReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnWindowKeyUp, RoutingStrategies.Tunnel);
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
        _refreshTimer.Tick += (_, _) => ViewModel?.Refresh();
        _clickTimer.Tick += (_, _) =>
        {
            _clickTimer.Stop();
            if (ViewModel is { IsInSession: true, HasMedia: true } viewModel)
            {
                viewModel.TogglePlayCommand.Execute(null);
            }
        };
        _volumeCloseTimer.Tick += (_, _) =>
        {
            _volumeCloseTimer.Stop();
            VolumePopup.IsOpen = false;
        };
        EventFeed.AddHandler(TappedEvent, (_, _) => ViewModel?.ToggleEventsCommand.Execute(null), handledEventsToo: true);
    }

    /// <summary>
    /// Gets the view model.
    /// </summary>
    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    /// <summary>
    /// Connects the window to its view model and player.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    /// <param name="player">The player host.</param>
    /// <param name="conference">Cameras and voices.</param>
    public void Initialize(MainWindowViewModel viewModel, PlayerHost player, Golether.Media.Conference.IConferenceMedia conference)
    {
        DataContext = viewModel;
        _cameras = new Controls.CameraRenderer(conference, viewModel);
        _timeline.Attach(viewModel);
        viewModel.Chat.LineAdded += (_, _) => Dispatcher.UIThread.Post(() => ChatScroll.ScrollToEnd(), DispatcherPriority.Background);
        ReactionOverlay.Opened += (_, _) =>
        {
            if (TopLevel.GetTopLevel(ReactionOverlay.Child) is not { } overlay)
            {
                return;
            }

            // Only the reactions themselves are visible: the window behind them is see-through.
            overlay.TransparencyLevelHint = [Avalonia.Controls.WindowTransparencyLevel.Transparent];
            overlay.Background = Avalonia.Media.Brushes.Transparent;

            // The overlay window must not catch the clicks meant for the video.
            if (overlay.TryGetPlatformHandle()?.Handle is { } handle)
            {
                Controls.ClickThroughWindow.Apply(handle);
            }
        };
        _player = player;
        Video.Player = player;
        player.BackendChanged += (_, _) => Dispatcher.UIThread.Post(UpdatePlayerState);
        if (OperatingSystem.IsWindows())
        {
            // mpv's window is disabled on Windows; the host window reports the clicks.
            Video.VideoPointer += (_, action) => OnVideoPointer(action);
        }
        else
        {
            player.VideoPointer += (_, action) => Dispatcher.UIThread.Post(() => OnVideoPointer(action));
        }
        viewModel.ComponentInstalled += (_, id) =>
        {
            if (id == Golether.Components.Catalog.ComponentId.Video)
            {
                player.Retry();
                UpdatePlayerState();
            }
        };
        UpdatePlayerState();
        _refreshTimer.Start();
    }

    /// <inheritdoc />
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || ViewModel is not { IsInSession: true } viewModel)
        {
            _refreshTimer.Stop();
            _clickTimer.Stop();
            _volumeCloseTimer.Stop();
            _cameras?.Dispose();
            return;
        }

        e.Cancel = true;
        _closeConfirmed = true;
        await viewModel.LeaveCommand.ExecuteAsync(null);
        Close();
    }

    /// <summary>
    /// Shows whether libmpv is used.
    /// </summary>
    private void UpdatePlayerState()
        => ViewModel?.SetPlayerState(_player?.IsAvailable == true, _player?.UnavailableReason);

    /// <summary>
    /// Toggles full screen.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event data.</param>
    private void OnToggleFullScreen(object? sender, RoutedEventArgs e) => ToggleFullScreen();

    /// <summary>
    /// Closes the track menu after a choice.
    /// </summary>
    /// <param name="sender">The menu entry.</param>
    /// <param name="e">The event data.</param>
    private void OnTrackChosen(object? sender, RoutedEventArgs e) => CloseAfterClick(TracksButton);

    /// <summary>
    /// Closes the reaction menu after a choice.
    /// </summary>
    /// <param name="sender">The reaction.</param>
    /// <param name="e">The event data.</param>
    private void OnReactionChosen(object? sender, RoutedEventArgs e) => CloseAfterClick(ReactionsButton);

    /// <summary>
    /// Closes the menu of a button, but only after the click has been handled.
    /// </summary>
    /// <remarks>
    /// Closing it inside the click handler detaches the pressed entry from the tree, its bindings go away with it,
    /// and the command of the entry never runs.
    /// </remarks>
    /// <param name="owner">The button that owns the menu.</param>
    private static void CloseAfterClick(Button owner)
        => Dispatcher.UIThread.Post(() => owner.Flyout?.Hide(), DispatcherPriority.Input);

    /// <summary>
    /// Switches between full screen and the previous state.
    /// </summary>
    private void ToggleFullScreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            WindowState = _stateBeforeFullScreen;
        }
        else
        {
            _stateBeforeFullScreen = WindowState;
            WindowState = WindowState.FullScreen;
        }
    }

    /// <summary>
    /// Stops timeline updates while the user drags.
    /// </summary>
    /// <param name="sender">The slider.</param>
    /// <param name="e">The event data.</param>
    private void OnTimelinePressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.IsScrubbing = true;
        }
    }

    /// <summary>
    /// Seeks everybody to the released position.
    /// </summary>
    /// <param name="sender">The slider.</param>
    /// <param name="e">The event data.</param>
    private void OnTimelineReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (ViewModel is not { IsScrubbing: true } viewModel)
        {
            return;
        }

        viewModel.IsScrubbing = false;
        _timeline.JumpTo(Timeline.Value);
        viewModel.SeekToCommand.Execute(Timeline.Value);
    }

    /// <summary>
    /// A click on the video toggles playback, a double click toggles full screen. The single click waits briefly so a
    /// double click does not pause and resume the video.
    /// </summary>
    /// <param name="action">The mouse action reported by the player.</param>
    private void OnVideoPointer(VideoPointerAction action)
    {
        var now = DateTimeOffset.UtcNow;
        if (action == VideoPointerAction.DoubleClick)
        {
            _clickTimer.Stop();
            _lastDoubleClick = now;
            ToggleFullScreen();
        }
        else if (now - _lastDoubleClick > DoubleClickWindow)
        {
            _clickTimer.Stop();
            _clickTimer.Start();
        }

        // The native video window takes the keyboard focus on click; the shortcuts belong to the window.
        Activate();
        Focus();
    }

    /// <summary>
    /// Opens the volume scale when the pointer is over the speaker or the scale.
    /// </summary>
    /// <param name="sender">The speaker or the scale.</param>
    /// <param name="e">The event data.</param>
    private void OnVolumePointerEntered(object? sender, PointerEventArgs e)
    {
        _volumeCloseTimer.Stop();
        VolumePopup.IsOpen = true;
    }

    /// <summary>
    /// Closes the volume scale shortly after the pointer left, unless it moves onto the scale.
    /// </summary>
    /// <param name="sender">The speaker or the scale.</param>
    /// <param name="e">The event data.</param>
    private void OnVolumePointerExited(object? sender, PointerEventArgs e)
    {
        _volumeCloseTimer.Stop();
        _volumeCloseTimer.Start();
    }

    /// <summary>
    /// Changes the volume with the mouse wheel over the speaker or the scale.
    /// </summary>
    /// <param name="sender">The speaker or the scale.</param>
    /// <param name="e">The event data.</param>
    private void OnVolumeWheel(object? sender, PointerWheelEventArgs e)
    {
        if (ViewModel?.Volume is { } volume)
        {
            volume.Volume = Math.Clamp(Math.Round(volume.Volume + (Math.Sign(e.Delta.Y) * 5)), 0, 100);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Shows the participant menu for other participants only (voice volume for everybody, device switches for the
    /// host); the own tile has no menu.
    /// </summary>
    /// <remarks>
    /// Runs in the tunnel phase, before the tile opens its menu.
    /// </remarks>
    /// <param name="sender">The window.</param>
    /// <param name="e">The event data.</param>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is Avalonia.StyledElement { DataContext: ParticipantItemViewModel { IsLocal: true } })
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Keeps the release of Space from pressing a focused button: Space belongs to play and pause.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The event data.</param>
    private void OnWindowKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && ViewModel is { IsInSession: true } && e.Source is not TextBox)
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles keyboard shortcuts outside text inputs.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The event data.</param>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { IsInSession: true } viewModel || e.Source is TextBox)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                viewModel.TogglePlayCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Left:
                viewModel.SeekBackCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right:
                viewModel.SeekForwardCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F11:
                ToggleFullScreen();
                e.Handled = true;
                break;
            case Key.Escape when WindowState == WindowState.FullScreen:
                ToggleFullScreen();
                e.Handled = true;
                break;
        }
    }
}
