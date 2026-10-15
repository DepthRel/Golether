using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Golether.Core.Data.Enums;
using Golether.UI.Enums;
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
    /// The native window of the overlay above the video, or <c>0</c> while it is closed. The pen takes the mouse on
    /// it while it draws.
    /// </summary>
    private nint _overlayHandle;

    /// <summary>
    /// Moves the timeline smoothly.
    /// </summary>
    private readonly Controls.TimelineAnimator _timeline;

    /// <summary>
    /// How long a click on the video waits for a second click before it toggles playback.
    /// </summary>
    private static readonly TimeSpan _doubleClickWindow = TimeSpan.FromMilliseconds(320);

    /// <summary>
    /// Toggles playback after a single click on the video.
    /// </summary>
    private readonly DispatcherTimer _clickTimer = new() { Interval = _doubleClickWindow };

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
    /// How close to the top or bottom of the full-screen window the pointer brings the hidden bars back.
    /// </summary>
    private const double REVEAL_EDGE = 24;

    /// <summary>
    /// The cursor over the video while it is hidden.
    /// </summary>
    private static readonly Cursor _hiddenCursor = new(StandardCursorType.None);

    /// <summary>
    /// Decides when the bars and the cursor are shown in full screen.
    /// </summary>
    private readonly Controls.ChromeAutoHide _chrome = new(TimeProvider.System, ChromeDelay);

    /// <summary>
    /// The windows with the camera of one participant each.
    /// </summary>
    private readonly Dictionary<ParticipantItemViewModel, ParticipantVideoWindow> _participantWindows = [];

    /// <summary>
    /// Where the pointer was reported last, in window coordinates. A layout change under a resting pointer repeats
    /// the report, and that must not count as a movement.
    /// </summary>
    private Avalonia.Point? _lastPointer;

    /// <summary>
    /// Whether the bars lie over the video in layers of their own (full screen) instead of in the layout.
    /// </summary>
    private bool _barsOverlaid;

    /// <summary>
    /// How far the pointer must go with the button down before a tile is being dragged and not clicked.
    /// </summary>
    private const double DRAG_THRESHOLD = 6;

    /// <summary>
    /// The tile under the pressed pointer and where it was pressed, or <see langword="null"/>.
    /// </summary>
    private (Border Tile, Point Start)? _pressedTile;

    /// <summary>
    /// Whether the pressed tile is being dragged.
    /// </summary>
    private bool _draggingTile;

    /// <summary>
    /// The tile the dragged one would swap with, or <see langword="null"/>.
    /// </summary>
    private Border? _dropTile;

    /// <summary>
    /// Gets or sets how long the bars and the cursor stay after the pointer left or stopped. Read when a window is
    /// created; tests shorten it.
    /// </summary>
    internal static TimeSpan ChromeDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Gets the open windows with the camera of one participant each.
    /// </summary>
    internal IReadOnlyCollection<ParticipantVideoWindow> ParticipantWindows => _participantWindows.Values;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(PointerMovedEvent, (_, e) => ReportPointer(e.GetPosition(this)), RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerExited += (_, _) => ReportZone(PointerZone.Elsewhere);
        _chrome.Changed += (_, _) => ApplyChrome();
        foreach (var (bar, popup) in new[] { (TopBar, TopBarPopup), (PlayerBar, PlayerBarPopup) })
        {
            // The pointer over a layer is not over this window, so the bar reports it itself.
            bar.PointerMoved += (_, _) => ReportZone(PointerZone.Bars);
            bar.PointerExited += (_, _) => ReportZone(PointerZone.Elsewhere);
            bar.PropertyChanged += (_, e) =>
            {
                // The layer goes away once the bar has slid out of it, so it does not catch the clicks on the video.
                if (e.Property == Controls.SlideReveal.ShownProperty && _barsOverlaid && bar is { IsExpanded: false, Shown: <= 0 })
                {
                    popup.IsOpen = false;
                }
            };
            popup.Opened += (_, _) => MakeTransparent(popup);
        }

        SidePanel.SizeChanged += (_, _) => UpdateTileColumns();
        ParticipantList.AddHandler(PointerPressedEvent, OnTilePressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        ParticipantList.AddHandler(PointerMovedEvent, OnTileMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        ParticipantList.AddHandler(PointerReleasedEvent, OnTileReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        ParticipantList.AddHandler(PointerCaptureLostEvent, (_, _) => EndTileDrag(), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        VideoArea.DoubleTapped += OnVideoAreaDoubleTapped;
        SessionGrid.SizeChanged += (_, _) => ApplySidePanelWidth();
        LayoutSplitter.DragCompleted += (_, _) => ViewModel?.StoreSidePanelWidth(SessionGrid.ColumnDefinitions[2].ActualWidth);
        LayoutSplitter.AddHandler(PointerPressedEvent, OnSplitterPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        _timeline = new Controls.TimelineAnimator(Timeline);
        Timeline.AddHandler(PointerPressedEvent, OnTimelinePressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        Timeline.AddHandler(PointerReleasedEvent, OnTimelineReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnWindowKeyUp, RoutingStrategies.Tunnel);
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
        _refreshTimer.Tick += (_, _) =>
        {
            ViewModel?.Refresh();
            UpdateChrome();
        };
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

            // The overlay window must not catch the clicks meant for the video, except while the pen draws on it.
            if (overlay.TryGetPlatformHandle()?.Handle is { } handle)
            {
                Controls.ClickThroughWindow.Apply(handle);
                _overlayHandle = handle;
                Controls.ClickThroughWindow.SetClickThrough(handle, !viewModel.Drawing.IsPenActive);
            }
        };
        ReactionOverlay.Closed += (_, _) => _overlayHandle = 0;
        viewModel.Drawing.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Golether.UI.ViewModels.DrawingViewModel.IsPenActive) && _overlayHandle != 0)
            {
                Controls.ClickThroughWindow.SetClickThrough(_overlayHandle, !viewModel.Drawing.IsPenActive);
            }
        };
        _player = player;
        Video.Player = player;
        player.BackendChanged += (_, _) => Dispatcher.UIThread.Post(UpdatePlayerState);
        if (OperatingSystem.IsWindows())
        {
            // mpv's window is disabled on Windows; the host window reports the clicks and the movements.
            Video.VideoPointer += (_, action) => OnVideoPointer(action);
            Video.PointerMovedOverVideo += (_, position) => ReportPointer(this.PointToClient(position));
        }
        else
        {
            // Here mpv owns the pointer over the picture and tells only about clicks, so a click is what brings the
            // hidden bars back.
            player.VideoPointer += (_, action) => Dispatcher.UIThread.Post(() =>
            {
                ReportZone(PointerZone.Bars);
                ReportZone(PointerZone.Video);
                OnVideoPointer(action);
            });
        }

        viewModel.Participants.CollectionChanged += (_, _) =>
        {
            CloseOrphanedParticipantWindows();
            UpdateTileColumns();
        };
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainWindowViewModel.SidePanelWidth))
            {
                ApplySidePanelWidth();
            }
            else if (e.PropertyName is nameof(MainWindowViewModel.IsInSession) or nameof(MainWindowViewModel.AlwaysShowBars))
            {
                // Both decide what may hide; the timer would notice too, but only a moment later.
                UpdateChrome();
            }
        };
        ApplySidePanelWidth();
        viewModel.ComponentInstalled += (_, id) =>
        {
            if (id == Golether.Core.Data.Enums.ComponentId.Video)
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
            foreach (var window in _participantWindows.Values.ToArray())
            {
                window.Close();
            }

            _cameras?.Dispose();
            return;
        }

        e.Cancel = true;
        _closeConfirmed = true;
        try
        {
            await viewModel.LeaveCommand.ExecuteAsync(null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The window must close whatever leaving the session ran into; this method returns nothing, so an
            // exception here would end the application before it could release the rest.
            System.Diagnostics.Trace.WriteLine($"Golether: leaving the session on close failed: {ex.Message}");
        }

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

    /// <inheritdoc />
    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // The state may be set before the content exists.
        if (change.Property == WindowStateProperty && StatusBar is not null)
        {
            UpdateChrome();
        }
    }

    /// <summary>
    /// Tells the hiding logic what the window is doing and lets its time pass. Called on a timer and when the
    /// window enters or leaves full screen.
    /// </summary>
    private void UpdateChrome()
    {
        var inSession = ViewModel is { IsInSession: true };
        var fullScreen = inSession && WindowState == WindowState.FullScreen;

        // "Always show" keeps full screen as a plain large window: the bars in the layout, the video between them.
        var overlaid = fullScreen && ViewModel is { AlwaysShowBars: false };
        SetBarsOverlaid(overlaid);
        _chrome.SetCursorHiding(inSession);
        _chrome.SetBarsHiding(overlaid);
        _chrome.SetHeld(inSession && IsChromeHeld());
        _chrome.Tick();
        StatusBar.IsVisible = !fullScreen;
    }

    /// <summary>
    /// Moves the bars between the layout and the layers over the video.
    /// </summary>
    /// <param name="overlaid">Whether the bars lie over the video.</param>
    private void SetBarsOverlaid(bool overlaid)
    {
        if (_barsOverlaid == overlaid)
        {
            return;
        }

        _barsOverlaid = overlaid;
        if (overlaid)
        {
            TopBarSlot.Child = null;
            PlayerBarSlot.Child = null;
            TopBarLayer.Child = TopBar;
            PlayerBarLayer.Child = PlayerBar;
            TopBarPopup.IsOpen = PlayerBarPopup.IsOpen = TopBar.IsExpanded;
        }
        else
        {
            TopBarPopup.IsOpen = PlayerBarPopup.IsOpen = false;
            TopBarLayer.Child = null;
            PlayerBarLayer.Child = null;
            TopBarSlot.Child = TopBar;
            PlayerBarSlot.Child = PlayerBar;

            // In the layout the bars are simply there.
            TopBar.Snap(true);
            PlayerBar.Snap(true);
        }
    }

    /// <summary>
    /// Makes the window of a layer see-through, so a bar sliding out of it uncovers the video and not a blank box.
    /// </summary>
    /// <param name="popup">The layer.</param>
    private static void MakeTransparent(Avalonia.Controls.Primitives.Popup popup)
    {
        if (TopLevel.GetTopLevel(popup.Child) is { } layer)
        {
            layer.TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            layer.Background = Avalonia.Media.Brushes.Transparent;
        }
    }

    /// <summary>
    /// Checks whether something needs the bars right now: an open menu of the bars, a drag on the timeline, the pen,
    /// or the notice about a lost connection.
    /// </summary>
    /// <returns><see langword="true"/> while the bars must stay.</returns>
    private bool IsChromeHeld()
        => VolumePopup.IsOpen ||
            TracksButton.Flyout?.IsOpen == true ||
            ReactionsButton.Flyout?.IsOpen == true ||
            MoreActions.Flyout?.IsOpen == true ||
            ViewModel is { IsScrubbing: true } or { IsReconnecting: true } or { Drawing.IsPenActive: true };

    /// <summary>
    /// Shows or hides the bars and the cursor as the hiding logic decided.
    /// </summary>
    private void ApplyChrome()
    {
        if (_barsOverlaid)
        {
            if (_chrome.BarsVisible)
            {
                // The layers open first, so the bars slide into something that is already on screen.
                TopBarPopup.IsOpen = PlayerBarPopup.IsOpen = true;
            }

            TopBar.IsExpanded = _chrome.BarsVisible;
            PlayerBar.IsExpanded = _chrome.BarsVisible;
        }

        // Over the picture the cursor belongs to the native video window, over the placeholder to the panel.
        Video.CursorHidden = !_chrome.CursorVisible;
        VideoArea.Cursor = _chrome.CursorVisible ? null : _hiddenCursor;
    }

    /// <summary>
    /// Takes a position of the pointer in window coordinates, from the window itself or from the video.
    /// </summary>
    /// <param name="position">The position.</param>
    private void ReportPointer(Avalonia.Point position)
    {
        if (position == _lastPointer)
        {
            return;
        }

        _lastPointer = position;
        ReportZone(ZoneAt(position));
    }

    /// <summary>
    /// Tells the full-screen logic that the pointer moved.
    /// </summary>
    /// <param name="zone">Where the pointer is.</param>
    private void ReportZone(PointerZone zone)
    {
        if (_chrome.IsActive)
        {
            _chrome.PointerMoved(zone);
        }
    }

    /// <summary>
    /// Names the part of the window a point is in.
    /// </summary>
    /// <param name="position">The point in window coordinates.</param>
    /// <returns>The zone.</returns>
    private PointerZone ZoneAt(Avalonia.Point position)
    {
        var video = BoundsInWindow(VideoArea);

        // The top bar spans the window; the player bar lies under the video only, and the chat is beside it.
        if (position.Y < REVEAL_EDGE ||
            (position.Y > Bounds.Height - REVEAL_EDGE && position.X < video.Right) ||
            BoundsInWindow(TopBar).Contains(position) ||
            BoundsInWindow(PlayerBar).Contains(position))
        {
            return PointerZone.Bars;
        }

        return video.Contains(position) ? PointerZone.Video : PointerZone.Elsewhere;
    }

    /// <summary>
    /// Returns the rectangle of a control in window coordinates; empty while the control is not laid out.
    /// </summary>
    /// <param name="control">The control.</param>
    /// <returns>The rectangle.</returns>
    private Avalonia.Rect BoundsInWindow(Control control)
        => control.IsEffectivelyVisible && control.TranslatePoint(default, this) is { } origin
            ? new Avalonia.Rect(origin, control.Bounds.Size)
            : default;

    /// <summary>
    /// A double click on the video area toggles full screen also while no video is shown there: the native video
    /// window, which reports its own clicks, is hidden then.
    /// </summary>
    /// <param name="sender">The video area.</param>
    /// <param name="e">The event data.</param>
    private void OnVideoAreaDoubleTapped(object? sender, TappedEventArgs e)
    {
        // The buttons of the placeholder keep their clicks.
        if (ViewModel is not { IsInSession: true } || (e.Source as Avalonia.Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        ToggleFullScreen();
        e.Handled = true;
    }

    /// <summary>
    /// Sets the width of the participants column: the one the user dragged to, as far as the window has room for it.
    /// </summary>
    private void ApplySidePanelWidth()
    {
        var side = SessionGrid.ColumnDefinitions[2];
        var available = SessionGrid.Bounds.Width - SessionGrid.ColumnDefinitions[1].ActualWidth;
        var wanted = ViewModel?.SidePanelWidth ?? MainWindowViewModel.DEFAULT_SIDE_PANEL_WIDTH;

        // Before the first layout the grid has no width yet; the wanted width is fitted in when it gets one.
        var width = available > 0 ? MainWindowViewModel.ClampSidePanelWidth(wanted, available) : wanted;
        side.MinWidth = MainWindowViewModel.DEFAULT_SIDE_PANEL_WIDTH;
        side.MaxWidth = available > 0
            ? MainWindowViewModel.ClampSidePanelWidth(double.MaxValue, available)
            : double.PositiveInfinity;
        if (side.Width.Value != width || !side.Width.IsAbsolute)
        {
            side.Width = new GridLength(width);
        }
    }

    /// <summary>
    /// Puts the participant tiles in one column or two, as the number of participants and the width of the panel
    /// ask for.
    /// </summary>
    private void UpdateTileColumns()
    {
        if (ParticipantList.ItemsPanelRoot is Avalonia.Controls.Primitives.UniformGrid tiles)
        {
            tiles.Columns = MainWindowViewModel.TileColumnsFor(ViewModel?.Participants.Count ?? 0, SidePanel.Bounds.Width);
        }
    }

    /// <summary>
    /// Finds the participant tile a visual belongs to.
    /// </summary>
    /// <param name="visual">The visual, or <see langword="null"/>.</param>
    /// <returns>The tile, or <see langword="null"/>.</returns>
    private static Border? TileOf(Visual? visual)
        => visual?.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("tile"));

    /// <summary>
    /// Remembers the tile under the pressed left button: it may become a drag. Only with the adjustable layout.
    /// </summary>
    /// <param name="sender">The list of tiles.</param>
    /// <param name="e">The event data.</param>
    private void OnTilePressed(object? sender, PointerPressedEventArgs e)
    {
        EndTileDrag();
        if (ViewModel is { IsLayoutResizable: true } &&
            e.GetCurrentPoint(ParticipantList).Properties.IsLeftButtonPressed &&
            TileOf(e.Source as Visual) is { } tile)
        {
            _pressedTile = (tile, e.GetPosition(ParticipantList));
        }
    }

    /// <summary>
    /// Starts dragging the pressed tile once the pointer has gone far enough, and marks the tile under the pointer
    /// as the one to swap with.
    /// </summary>
    /// <param name="sender">The list of tiles.</param>
    /// <param name="e">The event data.</param>
    private void OnTileMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedTile is not { } pressed)
        {
            return;
        }

        var position = e.GetPosition(ParticipantList);
        if (!_draggingTile)
        {
            var moved = position - pressed.Start;
            if (Math.Abs(moved.X) < DRAG_THRESHOLD && Math.Abs(moved.Y) < DRAG_THRESHOLD)
            {
                return;
            }

            _draggingTile = true;
            // The mark goes on the box around the tile: the veil that dims it is the neighbour of the tile there.
            (pressed.Tile.Parent as Control)?.Classes.Add("dragged");
            ParticipantList.Cursor = new Cursor(StandardCursorType.DragMove);
            e.Pointer.Capture(ParticipantList);
        }

        var target = TileOf(ParticipantList.InputHitTest(position) as Visual);
        if (ReferenceEquals(target, pressed.Tile))
        {
            target = null;
        }

        if (!ReferenceEquals(target, _dropTile))
        {
            _dropTile?.Classes.Remove("dropTarget");
            _dropTile = target;
            _dropTile?.Classes.Add("dropTarget");
        }
    }

    /// <summary>
    /// Swaps the dragged tile with the one it was dropped on.
    /// </summary>
    /// <param name="sender">The list of tiles.</param>
    /// <param name="e">The event data.</param>
    private void OnTileReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggingTile &&
            _pressedTile?.Tile.DataContext is ParticipantItemViewModel dragged &&
            _dropTile?.DataContext is ParticipantItemViewModel target)
        {
            ViewModel?.SwapParticipants(dragged, target);
        }

        var wasDragging = _draggingTile;
        EndTileDrag();
        if (wasDragging)
        {
            e.Pointer.Capture(null);
        }
    }

    /// <summary>
    /// Forgets the pressed tile and takes the marks of a drag away.
    /// </summary>
    private void EndTileDrag()
    {
        (_pressedTile?.Tile.Parent as Control)?.Classes.Remove("dragged");
        _dropTile?.Classes.Remove("dropTarget");
        _pressedTile = null;
        _dropTile = null;
        if (_draggingTile)
        {
            _draggingTile = false;
            ParticipantList.Cursor = null;
        }
    }

    /// <summary>
    /// A double click on the border between the video and the participants puts it back where the layout was
    /// designed.
    /// </summary>
    /// <remarks>
    /// Runs in the tunnel phase, before the border starts a drag.
    /// </remarks>
    /// <param name="sender">The border.</param>
    /// <param name="e">The event data.</param>
    private void OnSplitterPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ViewModel?.ResetSidePanelWidth();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Opens the camera of a participant in a window of its own, or brings that window to the front.
    /// </summary>
    /// <param name="sender">The menu entry of the participant tile.</param>
    /// <param name="e">The event data.</param>
    private void OnPopOutParticipant(object? sender, RoutedEventArgs e)
    {
        if ((sender as Avalonia.StyledElement)?.DataContext is not ParticipantItemViewModel participant)
        {
            return;
        }

        if (_participantWindows.TryGetValue(participant, out var open))
        {
            open.Activate();
            return;
        }

        var window = new ParticipantVideoWindow { DataContext = participant };
        window.Closed += (_, _) => _participantWindows.Remove(participant);
        _participantWindows[participant] = window;
        window.Show();
    }

    /// <summary>
    /// Closes the windows of participants who left; the end of the session closes them all.
    /// </summary>
    private void CloseOrphanedParticipantWindows()
    {
        foreach (var (participant, window) in _participantWindows.ToArray())
        {
            if (ViewModel?.Participants.Contains(participant) != true)
            {
                window.Close();
            }
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
        var now = TimeProvider.System.GetUtcNow();
        if (action == VideoPointerAction.DoubleClick)
        {
            _clickTimer.Stop();
            _lastDoubleClick = now;
            ToggleFullScreen();
        }
        else if (now - _lastDoubleClick > _doubleClickWindow)
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
