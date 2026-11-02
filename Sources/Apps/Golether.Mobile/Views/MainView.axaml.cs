using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Golether.Client.Controls;
using Golether.Client.ViewModels;
using Golether.Mobile.Controls;
using Golether.Mobile.ViewModels;

namespace Golether.Mobile.Views;

/// <summary>
/// The root view of the mobile client.
/// </summary>
public sealed partial class MainView : UserControl
{
    /// <summary>
    /// The width of the chat beside the film when the screen lies on its side.
    /// </summary>
    private const double SIDE_WIDTH = 300;

    /// <summary>
    /// The least width of an item of the grid of pictures with its gap.
    /// </summary>
    private const double GRID_ITEM_WIDTH = 112;

    /// <summary>
    /// How far a finger goes along the picture to turn to the next one.
    /// </summary>
    private const double SWIPE_DISTANCE = 72;

    /// <summary>
    /// The enlargement of a double tap on a picture.
    /// </summary>
    private const double DOUBLE_TAP_ZOOM = 2.5;

    /// <summary>
    /// How long the controls stay over a playing film after the last touch.
    /// </summary>
    private static readonly TimeSpan _controlsLife = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a line of the chat stays over a film that has the whole screen.
    /// </summary>
    private static readonly TimeSpan _chatBubbleLife = TimeSpan.FromSeconds(6);

    /// <summary>
    /// How often the position on the screen is read from the player.
    /// </summary>
    private static readonly TimeSpan _refreshInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Reads the position while the view lives. This is no timer of the dispatcher: on Android those sleep when the screen
    /// has nothing to draw, and over a paused film or a picture the screen would stand still until the next touch.
    /// </summary>
    private ITimer? _refresh;

    /// <summary>
    /// Shows the frames of the cameras, once the conference is ready.
    /// </summary>
    private CameraRenderer? _cameras;

    /// <summary>
    /// Whether the screen lies on its side.
    /// </summary>
    private bool _landscape;

    /// <summary>
    /// Whether the controls are shown over the film.
    /// </summary>
    private bool _controlsShown = true;

    /// <summary>
    /// Whether the chat is open beside the film when the screen lies on its side.
    /// </summary>
    private bool _chatOpen;

    /// <summary>
    /// How much of the view the keyboard covers.
    /// </summary>
    private double _keyboardHeight;

    /// <summary>
    /// The panel open over the screen, or <see langword="null"/>.
    /// </summary>
    private BottomSheet? _openSheet;

    /// <summary>
    /// When the line of the chat over the film came.
    /// </summary>
    private long _chatBubbleSince;

    /// <summary>
    /// When the screen was last touched, on the clock of <see cref="TimeProvider.System"/>.
    /// </summary>
    private long _lastTouch;

    /// <summary>
    /// Where a finger came down on the picture, or <see langword="null"/> while it is not there.
    /// </summary>
    private Point? _swipeFrom;

    /// <summary>
    /// The enlargement of the pinch so far, as the gesture counts it from its start.
    /// </summary>
    private double _pinchScale = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainView"/> class.
    /// </summary>
    public MainView()
    {
        InitializeComponent();

        Timeline.AddHandler(PointerPressedEvent, OnTimelinePressed, RoutingStrategies.Tunnel);
        Timeline.AddHandler(PointerReleasedEvent, OnTimelineReleased, RoutingStrategies.Tunnel);
        VideoArea.Tapped += OnVideoTapped;
        DataContextChanged += OnDataContextChanged;
        foreach (var sheet in new[] { TracksSheet, ReactionsSheet, PersonSheet, PictureSheet, LanguageSheet })
        {
            sheet.Closed += OnSheetClosed;
        }

        GalleryGrid.SizeChanged += (_, e) => UpdateGridColumns(e.NewSize.Width);
        Picture.GestureRecognizers.Add(new PinchGestureRecognizer());
        Picture.AddHandler(PinchEvent, OnPicturePinch);
        Picture.AddHandler(PinchEndedEvent, (_, _) => _pinchScale = 1);
        Picture.DoubleTapped += OnPictureDoubleTapped;
        Picture.PointerPressed += OnPicturePressed;
        Picture.PointerReleased += OnPictureReleased;
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        _refresh = TimeProvider.System.CreateTimer(_ => Dispatcher.UIThread.Post(OnRefresh), null, _refreshInterval, _refreshInterval);
        top.BackRequested += OnBackRequested;
        top.AddHandler(PointerPressedEvent, OnScreenTouched, RoutingStrategies.Tunnel, handledEventsToo: true);
        if (top.InputPane is { } keyboard)
        {
            keyboard.StateChanged += OnKeyboardChanged;
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (TopLevel.GetTopLevel(this) is { } top)
        {
            top.BackRequested -= OnBackRequested;
            top.RemoveHandler(PointerPressedEvent, OnScreenTouched);
            if (top.InputPane is { } keyboard)
            {
                keyboard.StateChanged -= OnKeyboardChanged;
            }
        }

        _refresh?.Dispose();
        _refresh = null;
        _cameras?.Dispose();
        _cameras = null;
    }

    /// <inheritdoc />
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        var landscape = e.NewSize.Width > e.NewSize.Height;
        if (landscape != _landscape)
        {
            _landscape = landscape;
            (DataContext as MainViewModel)?.RestartCamera();
            (DataContext as MainViewModel)?.FitSubtitles(!landscape);
        }

        ApplyLayout(e.NewSize.Width);
    }

    /// <summary>
    /// Moves the parts of the session screen for the position of the screen.
    /// </summary>
    /// <param name="width">The width of the view.</param>
    private void ApplyLayout(double width)
    {
        var typing = _keyboardHeight > 0;
        var drawing = DataContext is MainViewModel { Drawing.IsPenActive: true };

        // The window is drawn under the keyboard, so the view makes room itself; the chat and the film stay on the
        // screen, and the controls come back with the end of typing.
        Root.Margin = new Thickness(0, 0, 0, _keyboardHeight);
        Tiles.IsVisible = !typing && !_landscape;
        Dock.IsVisible = !typing && !_landscape;
        Header.IsVisible = !_landscape;

        Grid.SetRow(VideoArea, _landscape ? 0 : 1);
        Grid.SetRowSpan(VideoArea, _landscape ? 3 : 1);
        VideoArea.Height = _landscape ? double.NaN : width * 9 / 16;

        // A pen draws on the film, and controls in its way would take the strokes.
        Controls.IsVisible = _controlsShown && !typing && !drawing;
        FilmTitle.IsVisible = _landscape;
        WideTools.IsVisible = _landscape;
        WideLeave.IsVisible = _landscape;
        PeopleStrip.IsVisible = _landscape;
        PenExit.IsVisible = _landscape && drawing;

        Grid.SetRow(Side, _landscape ? 0 : 2);
        Grid.SetRowSpan(Side, _landscape ? 3 : 1);
        Grid.SetColumn(Side, _landscape ? 1 : 0);
        Side.Width = _landscape ? SIDE_WIDTH : double.NaN;
        Side.IsVisible = !_landscape || _chatOpen || typing;

        if (!_landscape)
        {
            ChatBubble.IsVisible = false;
        }
    }

    /// <summary>
    /// Makes room for the keyboard or takes it back.
    /// </summary>
    /// <param name="sender">The keyboard.</param>
    /// <param name="e">The new state.</param>
    private void OnKeyboardChanged(object? sender, InputPaneStateEventArgs e)
    {
        _keyboardHeight = e.NewState == InputPaneState.Open ? e.EndRect.Height : 0;
        ApplyLayout(Bounds.Width);
    }

    /// <summary>
    /// Answers the back button of the system.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The event.</param>
    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (_openSheet is { } sheet)
        {
            sheet.Close();
            e.Handled = true;
        }
        else if (_landscape && _chatOpen)
        {
            _chatOpen = false;
            ApplyLayout(Bounds.Width);
            e.Handled = true;
        }
        else if (DataContext is MainViewModel model)
        {
            e.Handled = model.GoBack();
        }
    }

    /// <summary>
    /// Opens a panel over the screen, instead of the one open before.
    /// </summary>
    /// <param name="sheet">The panel.</param>
    private void Show(BottomSheet sheet)
    {
        _openSheet?.Close();
        _openSheet = sheet;
        sheet.Open();
    }

    /// <summary>
    /// Forgets the closed panel; another one may have opened meanwhile.
    /// </summary>
    /// <param name="sender">The panel.</param>
    /// <param name="e">The event.</param>
    private void OnSheetClosed(object? sender, EventArgs e)
    {
        if (ReferenceEquals(_openSheet, sender))
        {
            _openSheet = null;
        }
    }

    /// <summary>
    /// Opens the sound and the subtitles.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event.</param>
    private void OnTracksClicked(object? sender, RoutedEventArgs e) => Show(TracksSheet);

    /// <summary>
    /// Opens the reactions.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event.</param>
    private void OnReactionsClicked(object? sender, RoutedEventArgs e) => Show(ReactionsSheet);

    /// <summary>
    /// Opens the languages.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event.</param>
    private void OnLanguageClicked(object? sender, RoutedEventArgs e) => Show(LanguageSheet);

    /// <summary>
    /// Closes the languages after a choice.
    /// </summary>
    /// <param name="sender">The language.</param>
    /// <param name="e">The event.</param>
    private void OnLanguageChosen(object? sender, RoutedEventArgs e) => LanguageSheet.Close();

    /// <summary>
    /// Opens what can be done about a person.
    /// </summary>
    /// <param name="sender">The tile.</param>
    /// <param name="e">The event.</param>
    private void OnTileClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ParticipantItemViewModel person })
        {
            PersonSheet.Content = person;
            Show(PersonSheet);
        }
    }

    /// <summary>
    /// Closes the panel of a person after an action.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event.</param>
    private void OnPersonDone(object? sender, RoutedEventArgs e) => PersonSheet.Close();

    /// <summary>
    /// Opens the choice to look at a held item of the grid alone.
    /// </summary>
    /// <param name="sender">The item.</param>
    /// <param name="e">The event.</param>
    private void OnTileHeld(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control { DataContext: GalleryItemViewModel item })
        {
            PictureSheet.Content = item;
            Show(PictureSheet);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Closes the choice after it was made.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event.</param>
    private void OnPictureMenuDone(object? sender, RoutedEventArgs e) => PictureSheet.Close();

    /// <summary>
    /// Opens or closes the chat beside the film.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event.</param>
    private void OnChatClicked(object? sender, RoutedEventArgs e)
    {
        _chatOpen = !_chatOpen;
        ChatBubble.IsVisible = false;
        ApplyLayout(Bounds.Width);
    }

    /// <summary>
    /// Puts the invitation from the clipboard into its field.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event.</param>
    private async void OnPasteClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard || DataContext is not MainViewModel model)
        {
            return;
        }

        if (await clipboard.TryGetTextAsync() is { Length: > 0 } text)
        {
            model.OpenInvite(text.Trim());
        }
    }

    /// <summary>
    /// Keeps the view in step with the pen.
    /// </summary>
    /// <param name="sender">The pen.</param>
    /// <param name="e">The change.</param>
    private void OnDrawingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DrawingViewModel.IsPenActive))
        {
            ApplyLayout(Bounds.Width);
        }
    }

    /// <summary>
    /// Shows the last line of the chat over a film that has the whole screen.
    /// </summary>
    /// <param name="model">The model.</param>
    private void ShowChatBubble(MainViewModel model)
    {
        Dispatcher.UIThread.Post(ChatScroll.ScrollToEnd, DispatcherPriority.Background);

        if (!_landscape || _chatOpen || model.Chat.Lines.LastOrDefault() is not { IsFromOther: true } line)
        {
            return;
        }

        ChatBubbleSender.Text = line.SenderText;
        ChatBubbleSender.Foreground = ColorTextBrushConverter.Instance.Convert(line.SenderColor, typeof(IBrush), null, CultureInfo.InvariantCulture) as IBrush;
        ChatBubbleText.Text = line.Text;
        ChatBubble.IsVisible = true;
        _chatBubbleSince = TimeProvider.System.GetTimestamp();
    }

    /// <summary>
    /// Notes the touch: it keeps the controls on the screen.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The event.</param>
    private void OnScreenTouched(object? sender, PointerPressedEventArgs e) => _lastTouch = TimeProvider.System.GetTimestamp();

    /// <summary>
    /// Takes the controls away from a film that plays by itself: they cover the picture.
    /// </summary>
    /// <param name="model">The model.</param>
    private void HideIdleControls(MainViewModel model)
    {
        if (ChatBubble.IsVisible && TimeProvider.System.GetElapsedTime(_chatBubbleSince) >= _chatBubbleLife)
        {
            ChatBubble.IsVisible = false;
        }

        if (!_controlsShown || _keyboardHeight > 0 || !model.IsWatch ||
            TimeProvider.System.GetElapsedTime(_lastTouch) < _controlsLife)
        {
            return;
        }

        var busy = (model.IsPaused && !model.Gallery.ShowsPicture) || model.Drawing.IsPenActive || _openSheet is not null || _chatOpen;

        if (!busy)
        {
            _controlsShown = false;
            ApplyLayout(Bounds.Width);
        }
    }

    /// <summary>
    /// Shows or hides the controls over the film. A tap on a control is for the control.
    /// </summary>
    /// <param name="sender">The film.</param>
    /// <param name="e">The event.</param>
    private void OnVideoTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && (source.FindAncestorOfType<Button>(true) is not null || source.FindAncestorOfType<ToggleButton>(true) is not null || source.FindAncestorOfType<Slider>(true) is not null))
        {
            return;
        }

        _controlsShown = !_controlsShown;
        ApplyLayout(Bounds.Width);
    }

    /// <summary>
    /// Reads the state of the session and draws the current frame: a frame that came while another screen covered the
    /// video was not drawn, and on a pause no other frame follows it.
    /// </summary>
    private void OnRefresh()
    {
        if (DataContext is not MainViewModel model)
        {
            return;
        }

        model.Refresh();

        if (model.IsWatch)
        {
            Video.RequestNextFrameRendering();
        }

        HideIdleControls(model);
    }

    /// <summary>
    /// Connects the video view to the player of the model.
    /// </summary>
    /// <param name="sender">The view.</param>
    /// <param name="e">The event.</param>
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel model)
        {
            return;
        }

        Video.Player = model.Player;
        model.PlayerReady += (_, _) => Video.Player = model.Player;
        model.ConferenceReady += (_, conference) => _cameras = new CameraRenderer(conference, model);
        model.Chat.LineAdded += (_, _) => ShowChatBubble(model);
        model.Gallery.PropertyChanged += OnGalleryChanged;
        model.Drawing.PropertyChanged += OnDrawingChanged;
        model.PropertyChanged += OnModelChanged;
        ErrorToast.IsVisible = false;
    }

    /// <summary>
    /// Shows a failure over the session; the join screen has a line of its own for it.
    /// </summary>
    /// <param name="sender">The model.</param>
    /// <param name="e">The change.</param>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is MainViewModel model && e.PropertyName is nameof(MainViewModel.Error) or nameof(MainViewModel.Screen))
        {
            ErrorToast.IsVisible = model.IsWatch && model.Error.Length > 0;
        }
    }

    /// <summary>
    /// Puts a tapped item of the grid on everybody's screen.
    /// </summary>
    /// <param name="sender">The item.</param>
    /// <param name="e">The event.</param>
    private void OnTileTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: GalleryItemViewModel item })
        {
            item.ShowToAllCommand.Execute(null);
        }
    }

    /// <summary>
    /// Shows the row of the current picture when the grid opens.
    /// </summary>
    /// <param name="sender">The pictures.</param>
    /// <param name="e">The change.</param>
    private void OnGalleryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is GalleryViewModel { IsGridOpen: true } gallery && e.PropertyName == nameof(GalleryViewModel.IsGridOpen))
        {
            var row = gallery.ShownIndex / Math.Max(1, gallery.GridColumns);
            Dispatcher.UIThread.Post(() => GalleryGrid.ScrollIntoView(row), DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Fits as many items into a row of the grid as the width allows.
    /// </summary>
    /// <param name="width">The width of the grid.</param>
    private void UpdateGridColumns(double width)
    {
        if (DataContext is MainViewModel model)
        {
            model.Gallery.GridColumns = Math.Max(1, (int)(width / GRID_ITEM_WIDTH));
        }
    }

    /// <summary>
    /// Enlarges the picture under two fingers. The gesture counts from its start, the picture from its last step.
    /// </summary>
    /// <param name="sender">The picture.</param>
    /// <param name="e">The gesture.</param>
    private void OnPicturePinch(object? sender, PinchEventArgs e)
    {
        if (e.Scale <= 0)
        {
            return;
        }

        Picture.ZoomAt(e.ScaleOrigin, e.Scale / _pinchScale);
        _pinchScale = e.Scale;
        _swipeFrom = null;
    }

    /// <summary>
    /// Enlarges the picture where it was tapped twice, or fits it whole again.
    /// </summary>
    /// <param name="sender">The picture.</param>
    /// <param name="e">The event.</param>
    private void OnPictureDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Picture.Zoom > 1)
        {
            Picture.Reset();
        }
        else
        {
            Picture.ZoomAt(e.GetPosition(Picture), DOUBLE_TAP_ZOOM);
        }
    }

    /// <summary>
    /// Notes where a finger came down: an enlarged picture moves under it, a whole one may turn.
    /// </summary>
    /// <param name="sender">The picture.</param>
    /// <param name="e">The event.</param>
    private void OnPicturePressed(object? sender, PointerPressedEventArgs e)
        => _swipeFrom = Picture.Zoom > 1 ? null : e.GetPosition(Picture);

    /// <summary>
    /// Turns to the next or the previous item when the finger went along the picture.
    /// </summary>
    /// <param name="sender">The picture.</param>
    /// <param name="e">The event.</param>
    private void OnPictureReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_swipeFrom is not { } from || DataContext is not MainViewModel model)
        {
            return;
        }

        _swipeFrom = null;
        var moved = e.GetPosition(Picture) - from;
        if (Math.Abs(moved.X) < SWIPE_DISTANCE || Math.Abs(moved.X) < 2 * Math.Abs(moved.Y))
        {
            return;
        }

        var turn = moved.X < 0 ? model.Gallery.NextCommand : model.Gallery.PreviousCommand;
        turn.Execute(null);
    }

    /// <summary>
    /// Closes the list of reactions after a choice.
    /// </summary>
    /// <param name="sender">The reaction.</param>
    /// <param name="e">The event.</param>
    private void OnReactionChosen(object? sender, RoutedEventArgs e) => ReactionsSheet.Close();

    /// <summary>
    /// Closes the list of tracks after a choice.
    /// </summary>
    /// <param name="sender">The entry.</param>
    /// <param name="e">The event.</param>
    private void OnTrackChosen(object? sender, RoutedEventArgs e) => TracksSheet.Close();

    /// <summary>
    /// Stops the film from moving the timeline while the user holds it.
    /// </summary>
    /// <param name="sender">The timeline.</param>
    /// <param name="e">The event.</param>
    private void OnTimelinePressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel model)
        {
            model.IsDragging = true;
        }
    }

    /// <summary>
    /// Moves the film to where the user let the timeline go.
    /// </summary>
    /// <param name="sender">The timeline.</param>
    /// <param name="e">The event.</param>
    private void OnTimelineReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is MainViewModel model)
        {
            model.IsDragging = false;
            model.SeekTo(Timeline.Value);
        }
    }
}
