using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Golether.Client.Controls;
using Golether.Client.Services;
using Golether.Client.ViewModels;
using Golether.Core.Configuration;
using Golether.Localization;
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
    /// The room the title of the film takes above the people along its edge.
    /// </summary>
    private const double FILM_TITLE_ROOM = 56;

    /// <summary>
    /// The room the timeline and the tools take below the people when they are not measured yet.
    /// </summary>
    private const double TRANSPORT_ROOM = 96;

    /// <summary>
    /// The gap between the people along the edge of the film.
    /// </summary>
    private const double PEOPLE_GAP = 8;

    /// <summary>
    /// The smallest height of a person along the edge of the film.
    /// </summary>
    private const double MIN_PERSON_HEIGHT = 56;

    /// <summary>
    /// The largest height of a person along the edge of the film.
    /// </summary>
    private const double MAX_PERSON_HEIGHT = 112;

    /// <summary>
    /// The width-to-height ratio of a person along the edge of the film, that of most cameras.
    /// </summary>
    private const double PERSON_ASPECT = 4.0 / 3.0;

    /// <summary>
    /// How many lines of the chat stand over the film at once; a new one pushes the oldest out at the top.
    /// </summary>
    private const int MAX_BUBBLES = 4;

    /// <summary>
    /// How far a line of the chat rises while it fades out.
    /// </summary>
    private const double BUBBLE_LEAVE = 16;

    /// <summary>
    /// How far a reaction rises when it appears.
    /// </summary>
    private const double REACTION_START = 40;

    /// <summary>
    /// How far a reaction still rises while it fades out.
    /// </summary>
    private const double REACTION_FADE_RISE = 60;

    /// <summary>
    /// The height of a reaction with the name under it.
    /// </summary>
    private const double REACTION_HEIGHT = 70;

    /// <summary>
    /// The least rise of a reaction before it fades, on a low film.
    /// </summary>
    private const double MIN_REACTION_RISE = 40;

    /// <summary>
    /// The part of the height of the film, from the top, where a reaction starts to fade: a little below the middle.
    /// </summary>
    private const double REACTION_FADE_LINE = 0.55;

    /// <summary>
    /// How close to its last line the chat counts as being at the end.
    /// </summary>
    private const double CHAT_END_SLACK = 24;

    /// <summary>
    /// The part of the width of the film on each side where a double tap skips.
    /// </summary>
    private const double SKIP_SIDE = 0.4;

    /// <summary>
    /// How long the sign of a skip by a double tap stays.
    /// </summary>
    private static readonly TimeSpan _skipHintLife = TimeSpan.FromMilliseconds(650);

    /// <summary>
    /// How long the controls stay over a playing film after the last touch.
    /// </summary>
    private static readonly TimeSpan _controlsLife = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a line of the chat stays over a film that has the whole screen.
    /// </summary>
    private static readonly TimeSpan _chatBubbleLife = TimeSpan.FromSeconds(6);

    /// <summary>
    /// How long the lines of the chat over the film take to move.
    /// </summary>
    private static readonly TimeSpan _bubbleMove = TimeSpan.FromMilliseconds(260);

    /// <summary>
    /// How long the note about looking at the folder alone stays open before it folds into a chip.
    /// </summary>
    private static readonly TimeSpan _aloneNoteLife = TimeSpan.FromSeconds(8);

    /// <summary>
    /// The lines of the chat over the film, the oldest first, with the time each came.
    /// </summary>
    private readonly List<(Control Bubble, long Since)> _bubbles = [];

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
    /// The bars of the system and the cut-outs of the screen, or <see langword="null"/> where there are none.
    /// </summary>
    private IInsetsManager? _insets;

    /// <summary>
    /// How far a reaction rises before it starts to fade.
    /// </summary>
    private double _reactionRise = MIN_REACTION_RISE;

    /// <summary>
    /// The width of a person along the edge of the film.
    /// </summary>
    private double _personWidth;

    /// <summary>
    /// When the note about looking at the folder alone was opened.
    /// </summary>
    private long _aloneSince;

    /// <summary>
    /// When the screen was last touched, on the clock of <see cref="TimeProvider.System"/>.
    /// </summary>
    private long _lastTouch;

    /// <summary>
    /// Where a finger came down on the picture, or <see langword="null"/> while it is not there.
    /// </summary>
    private Point? _swipeFrom;

    /// <summary>
    /// Where a finger came down on a video of a folder, or <see langword="null"/> while it is not there.
    /// </summary>
    private Point? _filmSwipeFrom;

    /// <summary>
    /// Whether the user looked at the folder alone at the last change, so the note opens only when looking alone starts.
    /// </summary>
    private bool _wasAlone;

    /// <summary>
    /// Where the finger that moves the own picture in its frame was last, or <see langword="null"/>.
    /// </summary>
    private Point? _avatarDragAt;

    /// <summary>
    /// The enlargement of the pinch on the own picture so far, as the gesture counts it from its start.
    /// </summary>
    private double _avatarPinch = 1;

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

        // The view keeps clear of the bars of the system itself: on its side the film takes the whole screen, under
        // the bars too, and only the controls keep clear of a cut-out of the screen.
        TopLevel.SetAutoSafeAreaPadding(this, false);
        InviteInput.PastingFromClipboard += OnInvitePasting;
        Timeline.AddHandler(PointerPressedEvent, OnTimelinePressed, RoutingStrategies.Tunnel);
        Timeline.AddHandler(PointerReleasedEvent, OnTimelineReleased, RoutingStrategies.Tunnel);
        VideoArea.Tapped += OnVideoTapped;
        VideoArea.DoubleTapped += OnFilmDoubleTapped;
        VideoArea.AddHandler(PointerPressedEvent, OnFilmPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        VideoArea.AddHandler(PointerReleasedEvent, OnFilmReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        ReactionItems.ContainerPrepared += OnReactionPrepared;
        ChatScroll.ScrollChanged += (_, _) => ChatToEnd.IsVisible = !IsChatAtEnd();
        DataContextChanged += OnDataContextChanged;
        foreach (var sheet in new[]
        {
            TracksSheet, ReactionsSheet, PersonSheet, PictureSheet, AvatarSheet, LeaveSheet, LanguageSheet,
            InviteSheet, AdmissionSheet, RemoveSheet, MediaSheet,
        })
        {
            sheet.Closed += OnSheetClosed;
        }

        LeaveSheet.Closed += OnLeaveSheetClosed;
        InviteSheet.Closed += OnHostSheetClosed;
        AdmissionSheet.Closed += OnHostSheetClosed;
        RemoveSheet.Closed += OnHostSheetClosed;
        MediaSheet.Closed += OnHostSheetClosed;
        AvatarSheet.Closed += OnAvatarSheetClosed;
        AvatarCrop.GestureRecognizers.Add(new PinchGestureRecognizer());
        AvatarCrop.AddHandler(PinchEvent, OnAvatarPinch);
        AvatarCrop.AddHandler(PinchEndedEvent, (_, _) => _avatarPinch = 1);
        AvatarCrop.PointerPressed += OnAvatarPressed;
        AvatarCrop.PointerMoved += OnAvatarMoved;
        AvatarCrop.PointerReleased += (_, _) => _avatarDragAt = null;

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

        if (top.InsetsManager is { } insets)
        {
            _insets = insets;
            insets.DisplayEdgeToEdgePreference = true;
            insets.SafeAreaChanged += OnSafeAreaChanged;
            ApplyLayout(Bounds.Width);
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

        if (_insets is { } insets)
        {
            insets.SafeAreaChanged -= OnSafeAreaChanged;
            _insets = null;
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
        var immersive = _landscape && DataContext is MainViewModel { IsWatch: true };
        var safe = _insets?.SafeAreaPadding ?? default;

        // On its side the film has the whole screen: the bars of the system stay away for good, so nothing moves when
        // the controls come and go, and only the controls keep clear of a cut-out of the screen. Elsewhere the view
        // keeps clear of the bars. The window is drawn under the keyboard, so the view makes room itself; the chat and
        // the film stay on the screen, and the controls come back with the end of typing.
        if (_insets is { } insets && insets.IsSystemBarVisible != !immersive)
        {
            insets.IsSystemBarVisible = !immersive;
        }

        Root.Margin = immersive
            ? new Thickness(0, 0, 0, _keyboardHeight)
            : new Thickness(safe.Left, safe.Top, safe.Right, safe.Bottom + _keyboardHeight);
        Controls.Margin = immersive ? safe : default;

        // The picture fills the cut-out of the screen as well, so the shade under the controls reaches over it too:
        // otherwise a strip of a light picture stays bright beside the darkened rest.
        ControlsShade.Margin = immersive ? new Thickness(-safe.Left, -safe.Top, -safe.Right, -safe.Bottom) : default;
        Side.Margin = immersive ? new Thickness(0, safe.Top, safe.Right, safe.Bottom) : default;
        Tiles.IsVisible = !typing && !_landscape;
        Dock.IsVisible = !typing && !_landscape;
        Header.IsVisible = !_landscape;

        Grid.SetRow(VideoArea, _landscape ? 0 : 1);
        Grid.SetRowSpan(VideoArea, _landscape ? 3 : 1);
        VideoArea.Height = _landscape ? double.NaN : width * 9 / 16;

        // A pen draws on the film, and controls in its way would take the strokes.
        Controls.IsVisible = _controlsShown && !typing && !drawing;
        FilmTitle.IsVisible = _landscape;
        FilmHead.Margin = _landscape ? new Thickness(20, 12) : new Thickness(12, 8);
        WideTools.IsVisible = _landscape;
        PeopleScroll.IsVisible = _landscape;
        PenExit.IsVisible = _landscape && drawing;
        ChatToggle.IsChecked = _chatOpen;
        if (_landscape)
        {
            FitPeople(safe);
        }

        FitReactions(safe, _landscape ? Bounds.Height - _keyboardHeight : width * 9 / 16);

        Grid.SetRow(Side, _landscape ? 0 : 2);
        Grid.SetRowSpan(Side, _landscape ? 3 : 1);
        Grid.SetColumn(Side, _landscape ? 1 : 0);
        Side.Width = _landscape ? SIDE_WIDTH : double.NaN;
        Side.IsVisible = !_landscape || _chatOpen || typing;

        if (!_landscape)
        {
            ClearBubbles();
        }
    }

    /// <summary>
    /// Sizes the people along the edge of the film: as large as the height between the title and the timeline allows,
    /// so the cameras can be made out.
    /// </summary>
    /// <param name="safe">The part of the screen the bars and the cut-outs take.</param>
    private void FitPeople(Thickness safe)
    {
        var count = Math.Max(1, (DataContext as MainViewModel)?.Participants.Count ?? 1);
        var transport = Transport.Bounds.Height > 0 ? Transport.Bounds.Height + PEOPLE_GAP : TRANSPORT_ROOM;
        var room = Bounds.Height - safe.Top - safe.Bottom - FILM_TITLE_ROOM - transport;
        var height = Math.Clamp((room - ((count - 1) * PEOPLE_GAP)) / count, MIN_PERSON_HEIGHT, MAX_PERSON_HEIGHT);
        _personWidth = Math.Round(height * PERSON_ASPECT);

        // More people than the height holds scroll under a finger.
        PeopleScroll.Margin = new Thickness(0, FILM_TITLE_ROOM, 12, transport);
        PeopleScroll.MaxHeight = Math.Max(room, MIN_PERSON_HEIGHT);
        PeopleStrip.Resources["PeopleTileHeight"] = Math.Round(height);
        PeopleStrip.Resources["PeopleTileWidth"] = _personWidth;
    }

    /// <summary>
    /// Gives the reactions their room: left of the people along the edge, from the bottom of the film to a little
    /// below its middle, where they fade.
    /// </summary>
    /// <param name="safe">The part of the screen the bars and the cut-outs take.</param>
    /// <param name="filmHeight">The height of the film.</param>
    private void FitReactions(Thickness safe, double filmHeight)
    {
        var bottom = _landscape ? safe.Bottom + FILM_TITLE_ROOM / 2 : PEOPLE_GAP;
        var right = _landscape ? safe.Right + 12 + _personWidth + PEOPLE_GAP : PEOPLE_GAP;
        var fadeFromBottom = filmHeight * (1 - REACTION_FADE_LINE);

        _reactionRise = Math.Max(MIN_REACTION_RISE, fadeFromBottom - bottom - (REACTION_HEIGHT / 2));
        ReactionArea.Margin = new Thickness(0, 0, right, bottom);
        ReactionArea.Height = _reactionRise + REACTION_FADE_RISE + REACTION_HEIGHT;
    }

    /// <summary>
    /// Lets a new reaction rise and fade as on the desktop, as far as the film on this screen allows.
    /// </summary>
    /// <param name="sender">The reactions.</param>
    /// <param name="e">The new reaction.</param>
    private void OnReactionPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        var reaction = e.Container;
        reaction.Opacity = 0;

        var rise = new Animation
        {
            Duration = ChatViewModel.ReactionLifetime,
            FillMode = FillMode.Forward,
            Children =
            {
                ReactionFrame(0, 0, 0, 0.6),
                ReactionFrame(0.12, 1, -REACTION_START, 1.1),
                ReactionFrame(0.25, 1, -REACTION_START - ((_reactionRise - REACTION_START) * 13 / 58), 1),
                ReactionFrame(0.7, 1, -_reactionRise, 1),
                ReactionFrame(1, 0, -_reactionRise - REACTION_FADE_RISE, 1),
            },
        };
        _ = rise.RunAsync(reaction);
    }

    /// <summary>
    /// Describes a moment of the flight of a reaction.
    /// </summary>
    /// <param name="cue">The share of the flight.</param>
    /// <param name="opacity">The opacity.</param>
    /// <param name="y">How far it has risen, negative upwards.</param>
    /// <param name="scale">Its size.</param>
    /// <returns>The moment.</returns>
    private static KeyFrame ReactionFrame(double cue, double opacity, double y, double scale) => new()
    {
        Cue = new Cue(cue),
        Setters =
        {
            new Setter(OpacityProperty, opacity),
            new Setter(TranslateTransform.YProperty, y),
            new Setter(ScaleTransform.ScaleXProperty, scale),
            new Setter(ScaleTransform.ScaleYProperty, scale),
        },
    };

    /// <summary>
    /// Follows the bars of the system and the cut-outs of the screen.
    /// </summary>
    /// <param name="sender">The insets manager.</param>
    /// <param name="e">The new safe area.</param>
    private void OnSafeAreaChanged(object? sender, SafeAreaChangedArgs e) => ApplyLayout(Bounds.Width);

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
    /// Opens the choice of an image for the own picture from the panel of the own person.
    /// </summary>
    /// <param name="sender">The picture.</param>
    /// <param name="e">The event.</param>
    private void OnOwnAvatarClicked(object? sender, RoutedEventArgs e)
    {
        PersonSheet.Close();
        (DataContext as MainViewModel)?.EditAvatarCommand.Execute(null);
    }

    /// <summary>
    /// Notes where the finger that moves the own picture came down.
    /// </summary>
    /// <param name="sender">The frame.</param>
    /// <param name="e">The event.</param>
    private void OnAvatarPressed(object? sender, PointerPressedEventArgs e) => _avatarDragAt = e.GetPosition(AvatarCrop);

    /// <summary>
    /// Moves the own picture in its frame after the finger.
    /// </summary>
    /// <param name="sender">The frame.</param>
    /// <param name="e">The event.</param>
    private void OnAvatarMoved(object? sender, PointerEventArgs e)
    {
        if (_avatarDragAt is not { } last || DataContext is not MainViewModel { AvatarEditor: { } editor })
        {
            return;
        }

        var now = e.GetPosition(AvatarCrop);
        editor.MoveBy(now.X - last.X, now.Y - last.Y);
        _avatarDragAt = now;
    }

    /// <summary>
    /// Enlarges the own picture under two fingers; the gesture counts from its start, the picture from its last step.
    /// </summary>
    /// <param name="sender">The frame.</param>
    /// <param name="e">The gesture.</param>
    private void OnAvatarPinch(object? sender, PinchEventArgs e)
    {
        if (e.Scale <= 0 || DataContext is not MainViewModel { AvatarEditor: { } editor })
        {
            return;
        }

        _avatarDragAt = null;
        editor.ZoomBy(e.Scale / _avatarPinch);
        _avatarPinch = e.Scale;
    }

    /// <summary>
    /// Takes the circle of the frame as the own picture: the largest size that fits a message of the session.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event.</param>
    private async void OnAvatarApplied(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel model)
        {
            return;
        }

        if (AvatarRendering.Render(AvatarCrop, AvatarEditorViewModel.CROP_SIZE) is not { } avatar)
        {
            model.Error = Texts.Get("Avatar.NotSaved.Title");
            AvatarSheet.Close();
            return;
        }

        await model.ApplyAvatarAsync(avatar);
    }

    /// <summary>
    /// Leaves the own picture as it was.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event.</param>
    private void OnAvatarCancelled(object? sender, RoutedEventArgs e) => AvatarSheet.Close();

    /// <summary>
    /// Gives up the frame however its panel was closed.
    /// </summary>
    /// <param name="sender">The panel.</param>
    /// <param name="e">The event.</param>
    private void OnAvatarSheetClosed(object? sender, EventArgs e) => (DataContext as MainViewModel)?.CancelAvatarCommand.Execute(null);

    /// <summary>
    /// Keeps the user in the session: the question to leave goes away.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event.</param>
    private void OnStayClicked(object? sender, RoutedEventArgs e) => LeaveSheet.Close();

    /// <summary>
    /// Forgets the question to leave once its panel is closed, however it was closed.
    /// </summary>
    /// <param name="sender">The panel.</param>
    /// <param name="e">The event.</param>
    private void OnLeaveSheetClosed(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel model)
        {
            model.IsLeaveAsked = false;
        }
    }

    /// <summary>
    /// Forgets what a panel of the host was open for once it is closed, however it was closed. A participant who waits
    /// to come in and whose panel was swept away is turned away: an answer is owed, and they may knock again.
    /// </summary>
    /// <param name="sender">The panel.</param>
    /// <param name="e">The event.</param>
    private void OnHostSheetClosed(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel model)
        {
            return;
        }

        if (ReferenceEquals(sender, InviteSheet))
        {
            model.Invitation = null;
        }
        else if (ReferenceEquals(sender, AdmissionSheet) && model.Admission is not null)
        {
            model.DeclineCommand.Execute(null);
        }
        else if (ReferenceEquals(sender, RemoveSheet))
        {
            model.RemovalAsked = null;
        }
        else if (ReferenceEquals(sender, MediaSheet))
        {
            model.IsMediaChangeOpen = false;
        }
    }

    /// <summary>
    /// Leaves what is shown as it is.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event.</param>
    private void OnMediaCancelled(object? sender, RoutedEventArgs e) => MediaSheet.Close();

    /// <summary>
    /// Opens a panel while the model has something for it and closes it when that is gone; a panel already open stays,
    /// with what it shows replaced.
    /// </summary>
    /// <param name="sheet">The panel.</param>
    /// <param name="wanted">Whether the model has something for it.</param>
    private void Follow(BottomSheet sheet, bool wanted)
    {
        if (wanted && !sheet.IsOpen)
        {
            Show(sheet);
        }
        else if (!wanted && sheet.IsOpen)
        {
            sheet.Close();
        }
    }

    /// <summary>
    /// Opens the page of the project in the browser.
    /// </summary>
    /// <param name="sender">The link.</param>
    /// <param name="e">The event.</param>
    private async void OnSiteClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
        {
            await launcher.LaunchUriAsync(new Uri(ProjectSite.BASE_URL));
        }
    }

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
        ClearBubbles();
        ApplyLayout(Bounds.Width);
    }

    /// <summary>
    /// Pastes into the field of the invitation what the system has in the clipboard: the clipboard of Avalonia sees
    /// plain text only, and a copied link is often kept as a link.
    /// </summary>
    /// <param name="sender">The field.</param>
    /// <param name="e">The event.</param>
    private void OnInvitePasting(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel model && model.ReadClipboard() is { } text && !string.IsNullOrWhiteSpace(text))
        {
            e.Handled = true;
            InviteInput.SelectedText = text.Trim();
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
    /// Follows a new line of the chat: the chat goes to it unless the user reads older lines (the key to the end shows
    /// then), and over a film that has the whole screen it joins the lines standing over the film.
    /// </summary>
    /// <param name="model">The model.</param>
    private void ShowChatBubble(MainViewModel model)
    {
        var line = model.Chat.Lines.LastOrDefault();
        if (IsChatAtEnd() || line is { IsOwn: true })
        {
            Dispatcher.UIThread.Post(ChatScroll.ScrollToEnd, DispatcherPriority.Background);
        }

        if (!_landscape || _chatOpen || line is not { IsFromOther: true })
        {
            return;
        }

        var bubble = new ContentControl { Content = line, ContentTemplate = (IDataTemplate)Resources["ChatBubble"]! };
        ChatBubbles.Children.Add(bubble);
        _bubbles.Add((bubble, TimeProvider.System.GetTimestamp()));
        while (_bubbles.Count > MAX_BUBBLES)
        {
            RetireBubble(_bubbles[0].Bubble);
        }

        LiftBubbles(bubble);
    }

    /// <summary>
    /// Brings a new line in at the bottom: the lines above it rise by its height instead of jumping.
    /// </summary>
    /// <param name="added">The new line.</param>
    private void LiftBubbles(Control added)
    {
        added.Measure(Size.Infinity);
        var lift = added.DesiredSize.Height + ChatBubbles.Spacing;

        var rise = new Animation
        {
            Duration = _bubbleMove,
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(TranslateTransform.YProperty, lift) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(TranslateTransform.YProperty, 0d) } },
            },
        };
        var appear = new Animation
        {
            Duration = _bubbleMove,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d) } },
            },
        };

        _ = rise.RunAsync(ChatBubbles);
        _ = appear.RunAsync(added);
    }

    /// <summary>
    /// Lets a line go: it fades out rising, and leaves the stack when it has gone.
    /// </summary>
    /// <param name="bubble">The line.</param>
    private async void RetireBubble(Control bubble)
    {
        _bubbles.RemoveAll(b => ReferenceEquals(b.Bubble, bubble));

        var leave = new Animation
        {
            Duration = _bubbleMove,
            FillMode = FillMode.Forward,
            Easing = new CubicEaseIn(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, -BUBBLE_LEAVE) } },
            },
        };

        await leave.RunAsync(bubble);
        ChatBubbles.Children.Remove(bubble);
    }

    /// <summary>
    /// Takes the lines of the chat off the film at once: the chat itself is on the screen, or the film is not.
    /// </summary>
    private void ClearBubbles()
    {
        _bubbles.Clear();
        ChatBubbles.Children.Clear();
    }

    /// <summary>
    /// Checks whether the chat shows its last line.
    /// </summary>
    /// <returns><see langword="true"/> when it does.</returns>
    private bool IsChatAtEnd() => ChatScroll.Extent.Height - ChatScroll.Viewport.Height - ChatScroll.Offset.Y <= CHAT_END_SLACK;

    /// <summary>
    /// Takes the chat to its last line.
    /// </summary>
    /// <param name="sender">The key.</param>
    /// <param name="e">The event.</param>
    private void OnChatToEndClicked(object? sender, RoutedEventArgs e) => ChatScroll.ScrollToEnd();

    /// <summary>
    /// Folds the note about looking at the folder alone into a chip, so the picture is free.
    /// </summary>
    /// <param name="sender">The cross.</param>
    /// <param name="e">The event.</param>
    private void OnAloneFolded(object? sender, RoutedEventArgs e) => FoldAloneNote(true);

    /// <summary>
    /// Opens the note about looking at the folder alone again, with its way back to everybody.
    /// </summary>
    /// <param name="sender">The chip.</param>
    /// <param name="e">The event.</param>
    private void OnAloneUnfolded(object? sender, RoutedEventArgs e) => FoldAloneNote(false);

    /// <summary>
    /// Folds or opens the note about looking at the folder alone; an open note folds by itself after a while.
    /// </summary>
    /// <param name="folded">Whether to fold it.</param>
    private void FoldAloneNote(bool folded)
    {
        AloneCard.IsVisible = !folded;
        AloneChip.IsVisible = folded;
        _aloneSince = TimeProvider.System.GetTimestamp();
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
        foreach (var (bubble, since) in _bubbles.ToArray())
        {
            if (TimeProvider.System.GetElapsedTime(since) >= _chatBubbleLife)
            {
                RetireBubble(bubble);
            }
        }

        if (AloneCard.IsVisible && model.Gallery.IsAlone && TimeProvider.System.GetElapsedTime(_aloneSince) >= _aloneNoteLife)
        {
            FoldAloneNote(true);
        }

        if (!_controlsShown || _keyboardHeight > 0 || !model.IsWatch ||
            TimeProvider.System.GetElapsedTime(_lastTouch) < _controlsLife)
        {
            return;
        }

        var busy = (!model.IsPlaying && !model.Gallery.ShowsPicture) || model.Drawing.IsPenActive || _openSheet is not null || _chatOpen;

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
        if (IsOnControl(e.Source))
        {
            return;
        }

        _controlsShown = !_controlsShown;
        ApplyLayout(Bounds.Width);
    }

    /// <summary>
    /// Skips the film back or forward on a double tap on its left or right side, as the buttons beside the play button
    /// do. The first of the two taps showed or hid the controls; the double tap leaves them as they were.
    /// </summary>
    /// <param name="sender">The film.</param>
    /// <param name="e">The event.</param>
    private void OnFilmDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsWatch: true, Gallery.ShowsPicture: false, Drawing.IsPenActive: false } model || IsOnControl(e.Source))
        {
            return;
        }

        _controlsShown = !_controlsShown;
        ApplyLayout(Bounds.Width);

        var x = e.GetPosition(VideoArea).X;
        var width = VideoArea.Bounds.Width;
        if (x < width * SKIP_SIDE)
        {
            model.SkipBackCommand.Execute(null);
            FlashSkip(SkipBackHint);
        }
        else if (x > width * (1 - SKIP_SIDE))
        {
            model.SkipForwardCommand.Execute(null);
            FlashSkip(SkipForwardHint);
        }

        e.Handled = true;
    }

    /// <summary>
    /// Lights the sign of a skip for a moment.
    /// </summary>
    /// <param name="hint">The sign.</param>
    private void FlashSkip(Control hint)
    {
        var flash = new Animation
        {
            Duration = _skipHintLife,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(0.15), Setters = { new Setter(OpacityProperty, 1d) } },
                new KeyFrame { Cue = new Cue(0.6), Setters = { new Setter(OpacityProperty, 1d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0d) } },
            },
        };
        _ = flash.RunAsync(hint);
    }

    /// <summary>
    /// Notes where a finger came down on a video of a folder: a swipe along it turns to the next or the previous item,
    /// as over a picture.
    /// </summary>
    /// <param name="sender">The film.</param>
    /// <param name="e">The event.</param>
    private void OnFilmPressed(object? sender, PointerPressedEventArgs e)
        => _filmSwipeFrom = DataContext is MainViewModel { Gallery.IsActive: true, Gallery.ShowsPicture: false, Drawing.IsPenActive: false } && !IsOnControl(e.Source)
            ? e.GetPosition(VideoArea)
            : null;

    /// <summary>
    /// Turns to the next or the previous item when the finger went along a video of a folder.
    /// </summary>
    /// <param name="sender">The film.</param>
    /// <param name="e">The event.</param>
    private void OnFilmReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_filmSwipeFrom is not { } from || DataContext is not MainViewModel model)
        {
            return;
        }

        _filmSwipeFrom = null;
        TurnBySwipe(model, e.GetPosition(VideoArea) - from);
    }

    /// <summary>
    /// Turns to the next item for a swipe to the left and to the previous one for a swipe to the right; a short or a
    /// steep move is no swipe.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="moved">How far the finger went.</param>
    private static void TurnBySwipe(MainViewModel model, Vector moved)
    {
        if (Math.Abs(moved.X) < SWIPE_DISTANCE || Math.Abs(moved.X) < 2 * Math.Abs(moved.Y))
        {
            return;
        }

        var turn = moved.X < 0 ? model.Gallery.NextCommand : model.Gallery.PreviousCommand;
        turn.Execute(null);
    }

    /// <summary>
    /// Checks whether a touch landed on a control over the film, which takes it for itself.
    /// </summary>
    /// <param name="source">Where the touch landed.</param>
    /// <returns><see langword="true"/> when it is a button, a switch, a scale or the people along the edge.</returns>
    private static bool IsOnControl(object? source)
        => source is Visual visual &&
            (visual.FindAncestorOfType<Button>(true) is not null ||
            visual.FindAncestorOfType<ToggleButton>(true) is not null ||
            visual.FindAncestorOfType<Slider>(true) is not null ||
            visual.FindAncestorOfType<ScrollViewer>(true) is not null);

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
        model.Participants.CollectionChanged += (_, _) => ApplyLayout(Bounds.Width);
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

        if (e.PropertyName == nameof(MainViewModel.Screen))
        {
            ApplyLayout(Bounds.Width);
        }

        if (sender is MainViewModel framing && e.PropertyName == nameof(MainViewModel.AvatarEditor))
        {
            // Another image chosen from inside the frame comes into the frame already open.
            if (framing.AvatarEditor is not null)
            {
                if (!AvatarSheet.IsOpen)
                {
                    Show(AvatarSheet);
                }
            }
            else
            {
                AvatarSheet.Close();
            }
        }

        if (sender is MainViewModel asked && e.PropertyName == nameof(MainViewModel.IsLeaveAsked))
        {
            if (asked.IsLeaveAsked)
            {
                Show(LeaveSheet);
            }
            else
            {
                LeaveSheet.Close();
            }
        }

        if (sender is not MainViewModel host)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Invitation):
                Follow(InviteSheet, host.Invitation is not null);
                break;
            case nameof(MainViewModel.Admission):
                Follow(AdmissionSheet, host.Admission is not null);
                break;
            case nameof(MainViewModel.RemovalAsked):
                Follow(RemoveSheet, host.RemovalAsked is not null);
                break;
            case nameof(MainViewModel.IsMediaChangeOpen):
                Follow(MediaSheet, host.IsMediaChangeOpen);
                break;
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
    /// Follows the shown folder: the row of the current picture when the grid opens, the enlargement and the controls
    /// of the film when a picture comes or goes, the note about looking alone.
    /// </summary>
    /// <param name="sender">The pictures.</param>
    /// <param name="e">The change.</param>
    private void OnGalleryChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The pen follows the enlargement of the picture; over the film that comes back nothing is enlarged.
        if (sender is GalleryViewModel { ShowsPicture: false } && e.PropertyName == nameof(GalleryViewModel.ShowsPicture))
        {
            Picture.Reset();
        }

        // A picture has no sound and no subtitles: their panel, open over the film, goes away with it.
        if (sender is GalleryViewModel { ShowsPicture: true } && e.PropertyName == nameof(GalleryViewModel.ShowsPicture))
        {
            TracksSheet.Close();
        }

        // The note opens in full once, when looking alone starts; turning from picture to picture alone keeps it folded.
        if (sender is GalleryViewModel alone && e.PropertyName == nameof(GalleryViewModel.IsAlone))
        {
            if (alone.IsAlone && !_wasAlone)
            {
                FoldAloneNote(false);
            }

            _wasAlone = alone.IsAlone;
        }

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
        TurnBySwipe(model, e.GetPosition(Picture) - from);
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
