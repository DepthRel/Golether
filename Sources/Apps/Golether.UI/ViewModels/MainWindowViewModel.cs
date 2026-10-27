using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Core.Data.Enums;
using Golether.Core.Data.Stores;
using Golether.Core.Enums;
using Golether.Core.Identity;
using Golether.Localization;
using Golether.Media.Conference;
using Golether.Media.Player;
using Golether.Media.Streaming.Gallery;
using Golether.Security.Invites;
using Golether.Session;
using Golether.Sync.Protocol;
using Golether.Transports.Tls;
using Golether.UI.Enums;
using Golether.UI.Services;
using Golether.UI.ViewModels.Dialogs;

namespace Golether.UI.ViewModels;

/// <summary>
/// The main window: the start screen and the running session.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    /// <summary>
    /// The setting with the user name.
    /// </summary>
    public const string DISPLAY_NAME_SETTING = "displayName";

    /// <summary>
    /// The setting that keeps the event feed shown or hidden.
    /// </summary>
    public const string EVENTS_EXPANDED_SETTING = "ui.eventsExpanded";

    /// <summary>
    /// The setting with the open tab of the side panel (<c>chat</c> or <c>events</c>).
    /// </summary>
    public const string SIDE_TAB_SETTING = "ui.sideTab";

    /// <summary>
    /// The setting that lets the user drag the border between the video and the participants (<c>true</c> or
    /// <c>false</c>).
    /// </summary>
    public const string LAYOUT_RESIZABLE_SETTING = "ui.layout.resizable";

    /// <summary>
    /// The setting with the width of the participants panel chosen by dragging the border.
    /// </summary>
    public const string SIDE_PANEL_WIDTH_SETTING = "ui.layout.sideWidth";

    /// <summary>
    /// The width of the participants panel the layout was designed with; a double click on the border and switching
    /// the adjustable layout off both come back to it. The panel is never narrower.
    /// </summary>
    public const double DEFAULT_SIDE_PANEL_WIDTH = 300;

    /// <summary>
    /// The width the video always keeps, whatever the panel was dragged to.
    /// </summary>
    public const double MIN_VIDEO_WIDTH = 480;

    /// <summary>
    /// The setting that keeps the top bar and the player bar in full screen instead of hiding them (<c>true</c> or
    /// <c>false</c>).
    /// </summary>
    public const string ALWAYS_SHOW_BARS_SETTING = "ui.fullscreen.alwaysShowBars";

    /// <summary>
    /// How many participants one column of tiles is kept for, however wide the panel is.
    /// </summary>
    public const int SINGLE_COLUMN_PARTICIPANTS = 3;

    /// <summary>
    /// How many times wider than designed the participants panel must be before the tiles go in two columns.
    /// </summary>
    public const double TWO_COLUMN_WIDTH_FACTOR = 3;

    /// <summary>
    /// The prefix of the settings with the local voice volume of a participant (by device identifier).
    /// </summary>
    public const string VOICE_VOLUME_SETTING_PREFIX = "voice.volume.";

    /// <summary>
    /// How long a voice volume change waits before it is stored.
    /// </summary>
    internal static TimeSpan VoiceVolumeSaveDelay { get; set; } = TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// The setting that allows opening the port on the router (<c>true</c> or <c>false</c>).
    /// </summary>
    public const string OPEN_ROUTER_PORT_SETTING = "network.openRouterPort";

    /// <summary>
    /// The setting with the rendezvous server of hosted sessions (<c>host:port</c>, empty for none).
    /// </summary>
    public const string RENDEZVOUS_SERVER_SETTING = "network.rendezvousServer";

    /// <summary>
    /// The setting with the way hosted sessions find their rendezvous server: a name of <see cref="RendezvousMode"/>.
    /// </summary>
    public const string RENDEZVOUS_MODE_SETTING = "network.rendezvousMode";

    /// <summary>
    /// The setting that keeps the folder the folder browser was left in, where it opens next time, also after a
    /// restart.
    /// </summary>
    public const string GALLERY_FOLDER_SETTING = "gallery.folder";

    /// <summary>
    /// The maximum number of event feed entries.
    /// </summary>
    private const int MAX_EVENTS = 50;

    /// <summary>
    /// The seek step of the back/forward buttons.
    /// </summary>
    private static readonly TimeSpan _seekStep = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The readiness rules of the host, used to name the participants playback waits for.
    /// </summary>
    private static readonly Golether.Sync.Engine.PlaybackAuthorityOptions _readinessOptions = new();

    /// <summary>
    /// The session service.
    /// </summary>
    private readonly ISessionService _session;

    /// <summary>
    /// The dialogs.
    /// </summary>
    private readonly IDialogService _dialogs;

    /// <summary>
    /// The settings.
    /// </summary>
    private readonly ISettingsStore _settings;

    /// <summary>
    /// The UI dispatcher.
    /// </summary>
    private readonly IUiDispatcher _dispatcher;

    /// <summary>
    /// The settings of the player on this device, or <see langword="null"/>.
    /// </summary>
    private readonly ILocalPlayerControls? _playerControls;

    /// <summary>
    /// Creates the tunnel dialog view model.
    /// </summary>
    private readonly Func<string, TunnelDialogViewModel> _tunnelDialogFactory;

    /// <summary>
    /// Cameras and voices.
    /// </summary>
    private readonly IConferenceMedia _conference;

    /// <summary>
    /// Keeps this device's chosen avatar between sessions, or <see langword="null"/> when avatars are not offered.
    /// </summary>
    private readonly IAvatarStore? _avatarStore;

    /// <summary>
    /// The sound of new chat messages, or <see langword="null"/> when sounds are not offered.
    /// </summary>
    private readonly ChatSoundNotifier? _chatSound;

    /// <summary>
    /// The avatar bitmaps received for each participant, applied to their tiles.
    /// </summary>
    private readonly Dictionary<PeerId, object?> _peerAvatars = [];

    /// <summary>
    /// This device's chosen avatar (PNG bytes), sent to a session as soon as it starts, or <see langword="null"/>.
    /// </summary>
    private byte[]? _ownAvatar;

    /// <summary>
    /// Whether a refresh is already queued.
    /// </summary>
    private int _refreshQueued;

    /// <summary>
    /// Remembers where films were stopped.
    /// </summary>
    private readonly ResumeTracker _resume;

    /// <summary>
    /// The time source.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Decodes pictures: those of a shown folder and the small pictures of the folder browser.
    /// </summary>
    private readonly IPictureDecoder _pictures;

    /// <summary>
    /// The folder the folder browser was left in, where it opens next time; <see langword="null"/> until the browser
    /// was opened in this run (then it is taken from the settings).
    /// </summary>
    private string? _lastGalleryFolder;

    /// <summary>
    /// The media whose stored position was looked up.
    /// </summary>
    private string? _resumeMedia;

    /// <summary>
    /// Saves diagnostic reports, or <see langword="null"/>.
    /// </summary>
    private readonly IDiagnosticsWriter? _diagnostics;

    /// <summary>
    /// Looks for new versions, or <see langword="null"/>.
    /// </summary>
    private readonly IUpdateService? _updates;

    /// <summary>
    /// The release offered to the user, or <see langword="null"/>.
    /// </summary>
    private UpdateInfo? _update;

    /// <summary>
    /// Counts voice volume changes, so only the last one is stored.
    /// </summary>
    private long _voiceVolumeVersion;

    /// <summary>
    /// Cancels the join that is waiting for the host, or <see langword="null"/> when no join is running.
    /// </summary>
    private CancellationTokenSource? _joinCancellation;

    /// <summary>
    /// Whether the user is being asked about an invitation that came from outside.
    /// </summary>
    private bool _offeringInvite;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindowViewModel"/> class.
    /// </summary>
    /// <param name="session">The session service.</param>
    /// <param name="dialogs">The dialogs.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="dispatcher">The UI dispatcher.</param>
    /// <param name="tunnelDialogFactory">Creates the tunnel dialog for a user name.</param>
    /// <param name="deviceFingerprint">The short fingerprint of this device.</param>
    /// <param name="conference">The conferencing backend.</param>
    /// <param name="components">The native components.</param>
    /// <param name="playerControls">The volume of the player on this device, or <see langword="null"/>.</param>
    /// <param name="diagnostics">Saves diagnostic reports, or <see langword="null"/> when they are not offered.</param>
    /// <param name="updates">Looks for new versions, or <see langword="null"/> when updates are not offered.</param>
    /// <param name="language">The language selector, or <see langword="null"/> when the language cannot be chosen.</param>
    /// <param name="avatarStore">Keeps the chosen avatar, or <see langword="null"/> when avatars are not offered.</param>
    /// <param name="timeProvider">The time source, or <see langword="null"/> for the system clock.</param>
    /// <param name="chatSound">The sound of new chat messages, or <see langword="null"/> when sounds are not offered.</param>
    /// <param name="pictures">Decodes the pictures of a shown folder; Skia when <see langword="null"/>.</param>
    public MainWindowViewModel(
        ISessionService session,
        IDialogService dialogs,
        ISettingsStore settings,
        IUiDispatcher dispatcher,
        Func<string, TunnelDialogViewModel> tunnelDialogFactory,
        string deviceFingerprint,
        IConferenceMedia conference,
        IComponentService components,
        ILocalPlayerControls? playerControls = null,
        IDiagnosticsWriter? diagnostics = null,
        IUpdateService? updates = null,
        LanguageViewModel? language = null,
        IAvatarStore? avatarStore = null,
        TimeProvider? timeProvider = null,
        ChatSoundNotifier? chatSound = null,
        IPictureDecoder? pictures = null)
    {
        ArgumentNullException.ThrowIfNull(components);
        _avatarStore = avatarStore;
        _chatSound = chatSound;
        _timeProvider = timeProvider ?? TimeProvider.System;
        Language = language;
        VideoComponent = new ComponentItemViewModel(ComponentId.Video, components, dialogs);
        ConferenceComponent = new ComponentItemViewModel(ComponentId.Conference, components, dialogs);
        ComponentItems = [VideoComponent, ConferenceComponent];
        components.Installed += (_, id) => dispatcher.Post(() =>
        {
            foreach (var item in ComponentItems)
            {
                item.Refresh();
            }

            OnPropertyChanged(nameof(HasMissingComponents));
            UpdateConferenceNotice();

            if (id == ComponentId.Conference && Devices is not null)
            {
                _ = Devices.RefreshAsync();
            }

            ComponentInstalled?.Invoke(this, id);
        });
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _tunnelDialogFactory = tunnelDialogFactory ?? throw new ArgumentNullException(nameof(tunnelDialogFactory));
        _conference = conference ?? throw new ArgumentNullException(nameof(conference));

        _playerControls = playerControls;
        Volume = playerControls is null ? null : new VolumeViewModel(playerControls, _settings);
        Tracks = playerControls is null ? null : new TracksViewModel(playerControls, _settings, _dialogs, _dispatcher);
        _resume = new ResumeTracker(_settings);
        _diagnostics = diagnostics;
        _updates = updates;

        Chat = new ChatViewModel(_session, _dispatcher);
        Chat.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatViewModel.HasUnread))
            {
                OnPropertyChanged(nameof(HighlightPanelToggle));
            }
        };
        Drawing = new DrawingViewModel(_session, _dispatcher);
        _pictures = pictures ?? new SkiaPictureCodec();
        Gallery = new GalleryViewModel(_session, _pictures, _timeProvider);
        Gallery.PictureChanged += (_, _) => Drawing.Reset();
        Gallery.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(GalleryViewModel.IsActive) or nameof(GalleryViewModel.ShowsPicture) or nameof(GalleryViewModel.IsGridOpen))
            {
                OnPropertyChanged(nameof(ShowVideoHost));
                OnPropertyChanged(nameof(ShowPictures));
                OnPropertyChanged(nameof(ShowPlaceholder));
                OnPropertyChanged(nameof(CanPickFirstFile));
                OnPropertyChanged(nameof(IsFilmOnScreen));
                OnPropertyChanged(nameof(ShowVideoOverlay));
            }
        };

        conference.MuteChanged += (_, _) => _dispatcher.Post(() =>
        {
            // The host may switch devices off; the toggles show what is really in effect.
            MicrophoneMuted = conference.MicrophoneMuted;
            CameraOff = conference.CameraOff;
            if (CameraOff && Participants.FirstOrDefault(p => p.IsLocal) is { } self)
            {
                self.CameraImage = null;
            }
        });
        conference.MicrophoneFailed += (_, _) => _dispatcher.Post(
            () => _ = _dialogs.ShowErrorAsync(Texts.Get("Main.Error.MicrophoneFailed.Title"), Texts.Get("Main.Error.MicrophoneFailed.Text")));
        conference.SpeakingChanged += (_, change) => _dispatcher.Post(() => ShowSpeaking(change));
        conference.VideoQualityChanged += (_, change) => _dispatcher.Post(() =>
        {
            if (Participants.FirstOrDefault(p => p.PeerId == change.Peer) is { } participant)
            {
                participant.WeakConnection = change.Quality == VideoQuality.Low;
            }
        });
        Devices = conference is ICaptureDeviceSelector selector ? new DevicesViewModel(selector, _settings) : null;
        DeviceFingerprint = deviceFingerprint;
        UpdateConferenceNotice();
        _session.Changed += (_, _) => QueueRefresh();
        _session.EventRaised += (_, e) => _dispatcher.Post(() => AddEvent(e));
        _session.AvatarReceived += (_, update) => _dispatcher.Post(() => ApplyAvatar(update));
        _session.ClosedByHost += (_, reason) => _dispatcher.Post(() =>
        {
            // The first sentence says what happened and is the whole message when nothing follows it.
            var cut = reason.IndexOf(". ", StringComparison.Ordinal);
            var heading = (cut < 0 ? reason : reason[..cut]).TrimEnd('.');
            _ = _dialogs.ShowMessageAsync(heading, cut < 0 ? string.Empty : reason[(cut + 2)..]);
        });
        Texts.Localizer.LanguageChanged += (_, _) => _dispatcher.Post(RefreshTexts);
    }

    /// <summary>
    /// Gets the language selector, or <see langword="null"/> when the language cannot be chosen.
    /// </summary>
    public LanguageViewModel? Language { get; }

    /// <summary>
    /// Words again everything the view models composed from texts, after the user chose another language. The texts
    /// written in the XAML follow the language by themselves.
    /// </summary>
    private void RefreshTexts()
    {
        foreach (var item in ComponentItems)
        {
            item.Refresh();
        }

        UpdateConferenceNotice();

        // A name the user wrote stays; the name offered by default follows the language.
        var offered = Texts.Get("Main.DefaultSessionName");
        if (NewSessionName == _defaultSessionName)
        {
            NewSessionName = offered;
        }

        _defaultSessionName = offered;
        OnPropertyChanged(nameof(PlayButtonText));
        OnPropertyChanged(nameof(VideoPlaceholder));
        OnPropertyChanged(nameof(JoinTitle));
        OnPropertyChanged(nameof(JoinText));
        OnPropertyChanged(nameof(ResumeText));
        OnPropertyChanged(nameof(MicrophoneText));
        OnPropertyChanged(nameof(CameraText));
        Chat.RefreshTexts();
        Volume?.RefreshTexts();
        Tracks?.Refresh();
        foreach (var participant in Participants)
        {
            participant.RefreshTexts();
        }

        _ = Devices?.RefreshAsync();
        Refresh();
    }

    /// <summary>
    /// Raised on the UI thread after a native component was installed.
    /// </summary>
    public event EventHandler<ComponentId>? ComponentInstalled;

    /// <summary>
    /// Gets the video component.
    /// </summary>
    public ComponentItemViewModel VideoComponent { get; }

    /// <summary>
    /// Gets the camera and voice component.
    /// </summary>
    public ComponentItemViewModel ConferenceComponent { get; }

    /// <summary>
    /// Gets all components.
    /// </summary>
    public IReadOnlyList<ComponentItemViewModel> ComponentItems { get; }

    /// <summary>
    /// Gets a value indicating whether a component is missing.
    /// </summary>
    public bool HasMissingComponents => ComponentItems.Any(c => !c.IsAvailable);

    /// <summary>
    /// Gets the participants.
    /// </summary>
    public ObservableCollection<ParticipantItemViewModel> Participants { get; } = [];

    /// <summary>
    /// Gets the event feed, newest first.
    /// </summary>
    public ObservableCollection<string> Events { get; } = [];

    /// <summary>
    /// Gets the fingerprint of this device.
    /// </summary>
    public string DeviceFingerprint { get; }

    /// <summary>
    /// Gets the version of the application as <c>major.minor.patch</c>, shown in the corner of the status bar.
    /// </summary>
    public string AppVersion { get; } = AppInfo.Version;

    /// <summary>
    /// Gets or sets the notice about camera and voice availability.
    /// </summary>
    [ObservableProperty]
    public partial string ConferenceNotice { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether cameras and voices work.
    /// </summary>
    [ObservableProperty]
    public partial bool ConferenceAvailable { get; set; }

    /// <summary>
    /// Gets the player volume of this device, or <see langword="null"/> without a player.
    /// </summary>
    public VolumeViewModel? Volume { get; }

    /// <summary>
    /// Gets the sound and subtitle tracks chosen on this device, or <see langword="null"/> without a player.
    /// </summary>
    public TracksViewModel? Tracks { get; }

    /// <summary>
    /// Gets the session chat and reactions.
    /// </summary>
    public ChatViewModel Chat { get; }

    /// <summary>
    /// Gets the pen: the strokes drawn over the video.
    /// </summary>
    public DrawingViewModel Drawing { get; }

    /// <summary>
    /// Gets or sets the width-to-height ratio of the picture, or <c>0</c> while it is not known. The strokes are
    /// placed inside the picture and not inside the window, so everybody draws over the same frame.
    /// </summary>
    [ObservableProperty]
    public partial double VideoAspect { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the firewall drops the participants before they reach the session.
    /// </summary>
    [ObservableProperty]
    public partial bool FirewallBlocked { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the administrator prompt for the firewall rule is open.
    /// </summary>
    [ObservableProperty]
    public partial bool IsAllowingFirewall { get; set; }

    /// <summary>
    /// Asks the firewall to let participants in.
    /// </summary>
    /// <returns>A task that completes when the rule was added or refused.</returns>
    [RelayCommand]
    private async Task AllowFirewallAsync()
    {
        if (IsAllowingFirewall)
        {
            return;
        }

        IsAllowingFirewall = true;
        try
        {
            if (!await _session.AllowFirewallAsync(CancellationToken.None))
            {
                await _dialogs.ShowMessageAsync(Texts.Get("Firewall.NotAdded.Title"), Texts.Get("Firewall.NotAdded.Text"));
            }
        }
        finally
        {
            IsAllowingFirewall = false;
            FirewallBlocked = _session.FirewallBlocked;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the side panel (chat or event feed) is shown.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowChat), nameof(ShowEvents), nameof(SidePanelArrow), nameof(HighlightPanelToggle))]
    public partial bool EventsExpanded { get; set; } = true;

    /// <summary>
    /// Gets or sets the tab of the side panel.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowChat), nameof(ShowEvents), nameof(IsChatTab), nameof(IsEventsTab))]
    public partial SideTab SideTab { get; set; }

    /// <summary>
    /// Gets a value indicating whether the chat is shown.
    /// </summary>
    public bool ShowChat => EventsExpanded && SideTab == SideTab.Chat;

    /// <summary>
    /// Gets a value indicating whether the event feed is shown.
    /// </summary>
    public bool ShowEvents => EventsExpanded && SideTab == SideTab.Events;

    /// <summary>
    /// Gets a value indicating whether the chat tab is selected.
    /// </summary>
    public bool IsChatTab => SideTab == SideTab.Chat;

    /// <summary>
    /// Gets a value indicating whether the event tab is selected.
    /// </summary>
    public bool IsEventsTab => SideTab == SideTab.Events;

    /// <summary>
    /// Gets the arrow of the side panel button.
    /// </summary>
    public string SidePanelArrow => EventsExpanded ? "▾" : "▸";

    /// <summary>
    /// Gets a value indicating whether the button of the side panel draws attention: the panel is hidden and new
    /// messages came to the chat. The event feed never asks for attention.
    /// </summary>
    public bool HighlightPanelToggle => !EventsExpanded && Chat.HasUnread;

    /// <summary>
    /// Gets the camera and microphone choice, or <see langword="null"/> when the backend cannot list devices.
    /// </summary>
    public DevicesViewModel? Devices { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the microphone is muted.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MicrophoneText))]
    public partial bool MicrophoneMuted { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the camera is off.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CameraText))]
    public partial bool CameraOff { get; set; }

    /// <summary>
    /// Gets the caption of the microphone button.
    /// </summary>
    public string MicrophoneText => Texts.Get(MicrophoneMuted ? "Main.Microphone.Off" : "Main.Microphone.On");

    /// <summary>
    /// Gets the caption of the camera button.
    /// </summary>
    public string CameraText => Texts.Get(CameraOff ? "Main.Camera.Off" : "Main.Camera.On");

    /// <summary>
    /// Shows or hides the side panel and remembers the choice.
    /// </summary>
    [RelayCommand]
    private void ToggleEvents()
    {
        EventsExpanded = !EventsExpanded;
        SaveSidePanel();
    }

    /// <summary>
    /// Opens a tab of the side panel; a click on the open tab hides the panel.
    /// </summary>
    /// <param name="tab">The tab.</param>
    [RelayCommand]
    private void ShowTab(SideTab tab)
    {
        if (tab == SideTab && EventsExpanded)
        {
            EventsExpanded = false;
        }
        else
        {
            SideTab = tab;
            EventsExpanded = true;
        }

        SaveSidePanel();
    }

    /// <summary>
    /// Remembers the side panel and tells the chat whether it is visible.
    /// </summary>
    private void SaveSidePanel()
    {
        _ = _settings.SetAsync(EVENTS_EXPANDED_SETTING, EventsExpanded ? "true" : "false", CancellationToken.None);
        _ = _settings.SetAsync(SIDE_TAB_SETTING, SideTab == SideTab.Events ? "events" : "chat", CancellationToken.None);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the border between the video and the participants can be dragged.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLayoutResizable { get; set; }

    /// <summary>
    /// Gets or sets the width the participants panel was dragged to. The window narrows it when the video would get
    /// less than <see cref="MIN_VIDEO_WIDTH"/>.
    /// </summary>
    [ObservableProperty]
    public partial double SidePanelWidth { get; set; } = DEFAULT_SIDE_PANEL_WIDTH;

    /// <summary>
    /// Fits a wanted width of the participants panel into the room there is: never narrower than the designed width,
    /// never so wide that the video gets less than <see cref="MIN_VIDEO_WIDTH"/>.
    /// </summary>
    /// <param name="wanted">The width asked for.</param>
    /// <param name="available">The width shared by the video and the panel.</param>
    /// <returns>The width to use.</returns>
    public static double ClampSidePanelWidth(double wanted, double available)
    {
        if (!double.IsFinite(wanted))
        {
            return DEFAULT_SIDE_PANEL_WIDTH;
        }

        var widest = double.IsFinite(available) ? Math.Max(DEFAULT_SIDE_PANEL_WIDTH, available - MIN_VIDEO_WIDTH) : DEFAULT_SIDE_PANEL_WIDTH;
        return Math.Clamp(wanted, DEFAULT_SIDE_PANEL_WIDTH, widest);
    }

    /// <summary>
    /// Switches the adjustable layout on or off and remembers it. Switching it off puts the border back where the
    /// layout was designed.
    /// </summary>
    /// <param name="value">Whether the border can be dragged.</param>
    public void SetLayoutResizable(bool value)
    {
        if (IsLayoutResizable == value)
        {
            return;
        }

        IsLayoutResizable = value;
        _ = _settings.SetAsync(LAYOUT_RESIZABLE_SETTING, value ? "true" : "false", CancellationToken.None);
        if (!value)
        {
            ResetSidePanelWidth();
            ResetParticipantOrder();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the top bar and the player bar stay in full screen, with the video
    /// between them, instead of lying over the video and hiding.
    /// </summary>
    [ObservableProperty]
    public partial bool AlwaysShowBars { get; set; }

    /// <summary>
    /// Keeps the bars in full screen or lets them hide, and remembers it.
    /// </summary>
    /// <param name="value">Whether the bars stay.</param>
    public void SetAlwaysShowBars(bool value)
    {
        if (AlwaysShowBars == value)
        {
            return;
        }

        AlwaysShowBars = value;
        _ = _settings.SetAsync(ALWAYS_SHOW_BARS_SETTING, value ? "true" : "false", CancellationToken.None);
    }

    /// <summary>
    /// Tells how many columns the participant tiles go in: one, or two when there are more participants than one
    /// column is kept for and the panel was dragged wide enough for the tiles to stay large.
    /// </summary>
    /// <param name="participants">The number of participants.</param>
    /// <param name="panelWidth">The width of the participants panel.</param>
    /// <returns>The number of columns.</returns>
    public static int TileColumnsFor(int participants, double panelWidth)
        => participants > SINGLE_COLUMN_PARTICIPANTS && panelWidth > DEFAULT_SIDE_PANEL_WIDTH * TWO_COLUMN_WIDTH_FACTOR ? 2 : 1;

    /// <summary>
    /// Swaps the tiles of two participants. The order is only how this device shows them; nobody else sees it.
    /// </summary>
    /// <param name="first">A participant.</param>
    /// <param name="second">The participant to swap with.</param>
    public void SwapParticipants(ParticipantItemViewModel first, ParticipantItemViewModel second)
    {
        var from = Participants.IndexOf(first);
        var to = Participants.IndexOf(second);
        if (!IsLayoutResizable || from < 0 || to < 0 || from == to)
        {
            return;
        }

        // Two moves put each tile exactly where the other one was.
        Participants.Move(from, to);
        Participants.Move(from < to ? to - 1 : to + 1, from);
        _participantsReordered = true;
    }

    /// <summary>
    /// Puts the tiles back in the order of the session.
    /// </summary>
    private void ResetParticipantOrder()
    {
        if (!_participantsReordered)
        {
            return;
        }

        _participantsReordered = false;
        var views = _session.GetSnapshot()?.Participants;
        for (var i = 0; views is not null && i < views.Count; i++)
        {
            var peer = views[i].Info.PeerId;
            var current = Participants.Select((p, index) => (p, index)).FirstOrDefault(x => x.p.PeerId == peer);
            if (current.p is not null && current.index != i && i < Participants.Count)
            {
                Participants.Move(current.index, i);
            }
        }
    }

    /// <summary>
    /// Whether the user changed the order of the tiles, so new participants go to the end instead of into the order
    /// of the session.
    /// </summary>
    private bool _participantsReordered;

    /// <summary>
    /// Takes the width the participants panel was dragged to and remembers it.
    /// </summary>
    /// <param name="width">The width.</param>
    public void StoreSidePanelWidth(double width)
    {
        SidePanelWidth = double.IsFinite(width) ? Math.Max(DEFAULT_SIDE_PANEL_WIDTH, Math.Round(width)) : DEFAULT_SIDE_PANEL_WIDTH;
        _ = _settings.SetAsync(SIDE_PANEL_WIDTH_SETTING, SidePanelWidth.ToString("0", CultureInfo.InvariantCulture), CancellationToken.None);
    }

    /// <summary>
    /// Puts the border between the video and the participants back where the layout was designed.
    /// </summary>
    public void ResetSidePanelWidth() => StoreSidePanelWidth(DEFAULT_SIDE_PANEL_WIDTH);

    /// <summary>
    /// Keeps the chat informed whether it can be seen.
    /// </summary>
    /// <param name="value">The new value.</param>
    partial void OnEventsExpandedChanged(bool value) => Chat.IsExpanded = ShowChat;

    /// <summary>
    /// Keeps the chat informed whether it can be seen.
    /// </summary>
    /// <param name="value">The new value.</param>
    partial void OnSideTabChanged(SideTab value) => Chat.IsExpanded = ShowChat;

    /// <summary>
    /// Mutes or unmutes the microphone.
    /// </summary>
    [RelayCommand]
    private void ToggleMicrophone()
    {
        MicrophoneMuted = !MicrophoneMuted;
        _conference.SetMuted(MicrophoneMuted, CameraOff);
    }

    /// <summary>
    /// Turns the camera off or on.
    /// </summary>
    [RelayCommand]
    private void ToggleCamera()
    {
        CameraOff = !CameraOff;
        _conference.SetMuted(MicrophoneMuted, CameraOff);
        if (CameraOff && Participants.FirstOrDefault(p => p.IsLocal) is { } local)
        {
            local.CameraImage = null;
        }
    }

    /// <summary>
    /// Gets a value indicating whether choosing an avatar is offered.
    /// </summary>
    public bool CanChooseAvatar => _avatarStore is not null;

    /// <summary>
    /// Opens the settings window with the language, avatar, camera and microphone, and tunnel settings.
    /// </summary>
    /// <returns>A task that completes when the window is closed.</returns>
    [RelayCommand]
    private Task OpenSettingsAsync()
    {
        var settings = new Dialogs.SettingsViewModel(
            Language,
            Devices,
            ConferenceAvailable,
            ConferenceNotice,
            CanChooseAvatar,
            canConfigureTunnels: !IsInSession,
            DecodeOwnAvatar(),
            PickAvatarAsync,
            ApplyAvatarAsync,
            OpenTunnelsAsync,
            IsLayoutResizable,
            SetLayoutResizable,
            AlwaysShowBars,
            SetAlwaysShowBars,
            canChooseUpdates: _updates is not null,
            ChecksUpdates,
            UpdateUrl,
            SetUpdateSource,
            OpenAboutAsync,
            canChooseSounds: _chatSound is not null,
            chatSound: _chatSound?.Enabled ?? false,
            applyChatSound: _chatSound is null ? null : _chatSound.SetEnabled);

        // Reading the device lists asks the media backend and takes a moment, so it runs while the window is already
        // on screen. Refilling the lists empties the boxes, so the choice is put back once the lists are complete.
        _ = RefreshDevicesAsync(settings);
        return _dialogs.ShowSettingsAsync(settings);
    }

    /// <summary>
    /// Opens the window about the application, with the documents in the language of the application.
    /// </summary>
    /// <returns>A task that completes when the window is closed.</returns>
    private Task OpenAboutAsync()
    {
        var saveReport = CanSaveReport ? SaveReportAsync : (Func<Task>?)null;
        var about = new Dialogs.AboutViewModel(_dialogs, Texts.Localizer.Current.Code, saveReport);

        return _dialogs.ShowAboutAsync(about);
    }

    /// <summary>
    /// Re-reads the camera and microphone lists and shows the restored choice in the settings window.
    /// </summary>
    /// <param name="settings">The open settings window.</param>
    /// <returns>A task that completes when the lists are read.</returns>
    private async Task RefreshDevicesAsync(Dialogs.SettingsViewModel settings)
    {
        if (Devices is null)
        {
            return;
        }

        await Devices.RefreshAsync();
        settings.SyncDevices();
    }

    /// <summary>
    /// Opens the avatar chooser on the stored original, so the picture can be re-positioned. Nothing is stored yet:
    /// the settings window applies the result.
    /// </summary>
    /// <returns>The chosen avatar, or <see langword="null"/> when the chooser was cancelled.</returns>
    private async Task<AvatarSelection?> PickAvatarAsync()
    {
        if (_avatarStore is null)
        {
            return null;
        }

        var dialog = new Dialogs.AvatarDialogViewModel(_dialogs, await _avatarStore.LoadForEditingAsync());
        return await _dialogs.ShowAvatarAsync(dialog);
    }

    /// <summary>
    /// Stores a chosen avatar (round picture, original and framing) and, in a running session, sends it to everybody.
    /// </summary>
    /// <param name="selection">The chosen avatar.</param>
    /// <returns>A task that completes when the avatar is stored and sent.</returns>
    private async Task ApplyAvatarAsync(AvatarSelection selection)
    {
        if (_avatarStore is null)
        {
            return;
        }

        try
        {
            await _avatarStore.SaveAsync(selection);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(Texts.Get("Avatar.NotSaved.Title"), ex.Message);
            return;
        }

        _ownAvatar = selection.Avatar;
        if (_session.IsActive)
        {
            await _session.SetAvatarAsync(selection.Avatar, CancellationToken.None);
        }
    }

    /// <summary>
    /// Decodes this device's stored avatar into an image for previews, or <see langword="null"/> when there is none.
    /// </summary>
    /// <returns>The bitmap, or <see langword="null"/>.</returns>
    private object? DecodeOwnAvatar()
    {
        if (_ownAvatar is not { Length: > 0 } bytes)
        {
            return null;
        }

        try
        {
            return new Bitmap(new MemoryStream(bytes));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sends the chosen avatar to the session, if one was chosen.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the avatar was sent.</returns>
    private async Task SendStoredAvatarAsync(CancellationToken cancellationToken)
    {
        if (_ownAvatar is { Length: > 0 } png)
        {
            await _session.SetAvatarAsync(png, cancellationToken);
        }
    }

    /// <summary>
    /// Applies an avatar received for a participant to its tile, remembering it for tiles that appear later.
    /// </summary>
    /// <param name="update">The avatar.</param>
    private void ApplyAvatar(AvatarUpdate update)
    {
        object? image = null;
        if (update.Image is { Length: > 0 } bytes)
        {
            try
            {
                image = new Bitmap(new MemoryStream(bytes));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The bytes came from another device: a picture that cannot be decoded is simply not shown.
                image = null;
            }
        }

        if (image is null)
        {
            _peerAvatars.Remove(update.Peer);
        }
        else
        {
            _peerAvatars[update.Peer] = image;
        }

        if (Participants.FirstOrDefault(p => p.PeerId == update.Peer) is { } tile)
        {
            tile.AvatarImage = image;
        }
    }

    /// <summary>
    /// Shows whether cameras and voices work.
    /// </summary>
    private void UpdateConferenceNotice()
    {
        ConferenceAvailable = _conference.IsAvailable;
        ConferenceNotice = _conference.IsAvailable ? string.Empty : _conference.UnavailableReason ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the user name.
    /// </summary>
    [ObservableProperty]
    public partial string DisplayName { get; set; } = Environment.UserName;

    /// <summary>
    /// Gets or sets the name of a new session.
    /// </summary>
    [ObservableProperty]
    public partial string NewSessionName { get; set; } = Texts.Get("Main.DefaultSessionName");

    /// <summary>
    /// The name a new session gets when the user has not written one, in the language it was worded in.
    /// </summary>
    private string _defaultSessionName = Texts.Get("Main.DefaultSessionName");

    /// <summary>
    /// Gets or sets the listening port of a new session.
    /// </summary>
    [ObservableProperty]
    public partial decimal? Port { get; set; } = TlsTransportOptions.DEFAULT_PORT;

    /// <summary>
    /// Gets or sets a value indicating whether a new session asks the router to open its port.
    /// </summary>
    [ObservableProperty]
    public partial bool OpenRouterPort { get; set; } = true;

    /// <summary>
    /// Passes the router port choice to the sessions and remembers it.
    /// </summary>
    /// <param name="value">The new value.</param>
    partial void OnOpenRouterPortChanged(bool value)
    {
        _session.OpenRouterPort = value;
        _ = _settings.SetAsync(OPEN_ROUTER_PORT_SETTING, value ? "true" : "false", CancellationToken.None);
    }

    /// <summary>
    /// Gets or sets how hosted sessions find their rendezvous server.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRendezvousAutomatic), nameof(IsRendezvousDirect), nameof(IsRendezvousManual))]
    public partial RendezvousMode RendezvousMode { get; set; } = RendezvousMode.Automatic;

    /// <summary>
    /// Passes the way of finding the rendezvous server to the sessions and remembers it.
    /// </summary>
    /// <param name="value">The new value.</param>
    partial void OnRendezvousModeChanged(RendezvousMode value)
    {
        _session.RendezvousMode = value;
        _ = _settings.SetAsync(RENDEZVOUS_MODE_SETTING, value.ToString(), CancellationToken.None);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the application finds the rendezvous server by itself.
    /// </summary>
    public bool IsRendezvousAutomatic
    {
        get => RendezvousMode == RendezvousMode.Automatic;
        set => ChooseRendezvousMode(RendezvousMode.Automatic, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether hosted sessions use no rendezvous server.
    /// </summary>
    public bool IsRendezvousDirect
    {
        get => RendezvousMode == RendezvousMode.Direct;
        set => ChooseRendezvousMode(RendezvousMode.Direct, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether hosted sessions use the rendezvous server typed by the user.
    /// </summary>
    public bool IsRendezvousManual
    {
        get => RendezvousMode == RendezvousMode.Manual;
        set => ChooseRendezvousMode(RendezvousMode.Manual, value);
    }

    /// <summary>
    /// Takes the choice of one of the three switches; a switch that was only released chooses nothing.
    /// </summary>
    /// <param name="mode">The mode of the switch.</param>
    /// <param name="chosen">Whether the switch was turned on.</param>
    private void ChooseRendezvousMode(RendezvousMode mode, bool chosen)
    {
        if (chosen)
        {
            RendezvousMode = mode;
        }
    }

    /// <summary>
    /// Gets or sets the rendezvous server typed by the user as <c>host:port</c>, or an empty string for none.
    /// </summary>
    [ObservableProperty]
    public partial string RendezvousServer { get; set; } = string.Empty;

    /// <summary>
    /// Passes the rendezvous server to the sessions and remembers it. The address is checked when a session starts.
    /// </summary>
    /// <param name="value">The new value.</param>
    partial void OnRendezvousServerChanged(string value)
    {
        var trimmed = value.Trim();
        if (trimmed == _session.RendezvousServer)
        {
            return;
        }

        _session.RendezvousServer = trimmed;
        _ = _settings.SetAsync(RENDEZVOUS_SERVER_SETTING, trimmed, CancellationToken.None);
    }

    /// <summary>
    /// Gets or sets the invitation link to join.
    /// </summary>
    [ObservableProperty]
    public partial string InviteLink { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether an operation runs.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartHostingCommand), nameof(JoinCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// Gets or sets the progress text of a running operation.
    /// </summary>
    [ObservableProperty]
    public partial string BusyText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the running operation can be cancelled by the user (a join waiting for
    /// the host). The busy overlay and the approval screen then offer a cancel button.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowJoinScreen), nameof(JoinTitle), nameof(JoinText))]
    public partial bool IsCancellable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an invitation that came from outside (a link opened in the browser)
    /// is being taken up: from the moment the application learns of it until the user is asked and the join
    /// begins. Without a sign of it the start screen would look as if the link had done nothing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowJoinScreen), nameof(JoinTitle), nameof(JoinText))]
    public partial bool IsOpeningInvite { get; set; }

    /// <summary>
    /// Gets a value indicating whether the screen of a join in progress covers the window.
    /// </summary>
    public bool ShowJoinScreen => IsCancellable || IsOpeningInvite;

    /// <summary>
    /// Gets or sets a value indicating whether a session runs.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseLanguage))]
    public partial bool IsInSession { get; set; }

    /// <summary>
    /// Gets a value indicating whether the language selector is offered: on the start screen, not over the video.
    /// </summary>
    public bool CanChooseLanguage => Language is not null && !IsInSession;

    /// <summary>
    /// Gets or sets a value indicating whether this device hosts.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartNow))]
    public partial bool IsHost { get; set; }

    /// <summary>
    /// Gets or sets the session name.
    /// </summary>
    [ObservableProperty]
    public partial string SessionTitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the media caption.
    /// </summary>
    [ObservableProperty]
    public partial string MediaCaption { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether media is shared.
    /// </summary>
    [ObservableProperty]
    public partial bool HasMedia { get; set; }

    /// <summary>
    /// Gets or sets the synchronization chip text.
    /// </summary>
    [ObservableProperty]
    public partial string SyncText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the synchronization chip level.
    /// </summary>
    [ObservableProperty]
    public partial IndicatorLevel SyncLevel { get; set; }

    /// <summary>
    /// Gets or sets the security chip text.
    /// </summary>
    [ObservableProperty]
    public partial string SecurityText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the verification code shown while waiting for approval.
    /// </summary>
    [ObservableProperty]
    public partial string VerificationText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the host has not approved yet.
    /// </summary>
    [ObservableProperty]
    public partial bool IsAwaitingApproval { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the host was reached and is being asked to let this device in. Before
    /// that the join is still looking for a way to the host.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JoinTitle), nameof(JoinText))]
    public partial bool IsWaitingForHost { get; set; }

    /// <summary>
    /// Gets the heading of the screen shown while a session is being joined: what is happening right now.
    /// </summary>
    public string JoinTitle
        => Texts.Get(!IsCancellable && IsOpeningInvite ? "Main.Joining.Opening" : IsWaitingForHost ? "Main.Approval.Title" : "Main.Joining.Title");

    /// <summary>
    /// Gets the explanation under <see cref="JoinTitle"/>.
    /// </summary>
    public string JoinText
        => !IsCancellable && IsOpeningInvite ? string.Empty : Texts.Get(IsWaitingForHost ? "Main.Approval.Text" : "Main.Joining.Text");

    /// <summary>
    /// Gets or sets the playback position in seconds.
    /// </summary>
    [ObservableProperty]
    public partial double PositionSeconds { get; set; }

    /// <summary>
    /// Gets or sets the media duration in seconds.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimelineAdvance))]
    public partial double DurationSeconds { get; set; } = 1;

    /// <summary>
    /// Gets or sets the position text.
    /// </summary>
    [ObservableProperty]
    public partial string PositionText { get; set; } = "0:00:00";

    /// <summary>
    /// Gets or sets the duration text.
    /// </summary>
    [ObservableProperty]
    public partial string DurationText { get; set; } = "0:00:00";

    /// <summary>
    /// Gets or sets a value indicating whether the session plays.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlayIcon), nameof(ShowPauseIcon), nameof(PlayButtonText))]
    public partial bool IsPlaying { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the picture is really moving on this device: the session plays, the
    /// scheduled start has come, and the player is neither paused nor waiting for data. The timeline moves only then.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimelineAdvance))]
    public partial bool IsAdvancing { get; set; }

    /// <summary>
    /// Gets how fast the timeline moves between updates, in fractions of the film per second.
    /// </summary>
    public double TimelineAdvance => IsAdvancing ? 1 / DurationSeconds : 0;

    /// <summary>
    /// Gets or sets the parts of the film this device can play without waiting, as fractions of the duration.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<Controls.FractionRange> BufferedRanges { get; set; } = [];

    /// <summary>
    /// Computes the parts of the film this device can play without waiting.
    /// </summary>
    /// <param name="snapshot">The session.</param>
    /// <param name="duration">The duration.</param>
    /// <returns>The parts as fractions; the whole film when the file is on this device.</returns>
    internal static IReadOnlyList<Controls.FractionRange> ComputeBufferedRanges(SessionSnapshot snapshot, TimeSpan? duration)
    {
        if (snapshot.Media is null || duration is not { TotalSeconds: > 0 } total)
        {
            return [];
        }

        if (snapshot.IsHost || snapshot.UsesLocalCopy)
        {
            return [new Controls.FractionRange(0, 1)];
        }

        return snapshot.Local?.Snapshot.Buffered is { Count: > 0 } ranges
            ? [.. ranges.Select(r => new Controls.FractionRange(
                Math.Clamp(r.Start / total, 0, 1),
                Math.Clamp(r.End / total, 0, 1))).Where(r => r.End > r.Start)]
            : [];
    }

    /// <summary>
    /// Gets or sets a value indicating whether the film reached its end.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlayIcon), nameof(PlayButtonText))]
    public partial bool IsEnded { get; set; }

    /// <summary>
    /// Gets a value indicating whether the play triangle is shown (not playing and not at the end).
    /// </summary>
    public bool ShowPlayIcon => !IsPlaying && !IsEnded && !IsWaiting;

    /// <summary>
    /// Gets a value indicating whether the pause bars are shown (playing, or about to play once everybody is ready).
    /// </summary>
    public bool ShowPauseIcon => IsPlaying || IsWaiting;

    /// <summary>
    /// Gets or sets a value indicating whether playback waits until every participant is ready.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlayIcon), nameof(ShowPauseIcon), nameof(PlayButtonText), nameof(CanStartNow))]
    public partial bool IsWaiting { get; set; }

    /// <summary>
    /// Gets a value indicating whether the host may start without waiting.
    /// </summary>
    public bool CanStartNow => IsWaiting && IsHost;

    /// <summary>
    /// Gets or sets the state of an automatic reconnection, or an empty string.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReconnecting))]
    public partial string ReconnectText { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the connection to the host is being restored.
    /// </summary>
    public bool IsReconnecting => ReconnectText.Length > 0;

    /// <summary>
    /// Gets the name of the play button for tooltips and screen readers.
    /// </summary>
    public string PlayButtonText => Texts.Get(IsEnded ? "Main.Play.Again" : IsWaiting ? "Main.Play.CancelStart" : IsPlaying ? "Main.Play.Pause" : "Main.Play.Play");

    /// <summary>
    /// Gets or sets the countdown text of a scheduled start.
    /// </summary>
    [ObservableProperty]
    public partial string CountdownText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the countdown in a word: the seconds alone, shown when the whole text has no room.
    /// </summary>
    [ObservableProperty]
    public partial string CountdownShort { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the notice about the player backend.
    /// </summary>
    [ObservableProperty]
    public partial string PlayerNotice { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the status bar text about file sharing between participants, or an empty string.
    /// </summary>
    [ObservableProperty]
    public partial string SharingText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the status bar text about the clock.
    /// </summary>
    [ObservableProperty]
    public partial string ClockText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the latest event.
    /// </summary>
    [ObservableProperty]
    public partial string LastEvent { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether libmpv renders the video.
    /// </summary>
    [ObservableProperty]
    public partial bool PlayerAvailable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user drags the timeline.
    /// </summary>
    public bool IsScrubbing { get; set; }

    /// <summary>
    /// Gets a value indicating whether the native video area is shown. Avalonia cannot draw over it, so it is hidden
    /// while hints or progress must be visible.
    /// </summary>
    public bool ShowVideo => PlayerAvailable && HasMedia && !IsAwaitingApproval && !IsBusy;

    /// <summary>
    /// Gets a value indicating whether the native video window is shown: not while a picture of a folder or the grid
    /// of its items takes the screen, the film waits under them.
    /// </summary>
    public bool ShowVideoHost => ShowVideo && !ShowsGallerySurface;

    /// <summary>
    /// Gets a value indicating whether a picture of a shown folder or the grid of its items takes the screen.
    /// </summary>
    public bool ShowPictures => ShowsGallerySurface && !IsAwaitingApproval && !IsBusy;

    /// <summary>
    /// Gets a value indicating whether the window of reactions and strokes over the native video is open: for the
    /// whole session, except while a picture of a folder is shown and the layer inside the picture is used.
    /// </summary>
    public bool ShowVideoOverlay => IsInSession && !ShowPictures;

    /// <summary>
    /// Gets a value indicating whether the film is what this screen shows: no folder is shown, or a video of the
    /// folder is on the screen. The controls of the film, its sound and its subtitles are of use only then.
    /// </summary>
    public bool IsFilmOnScreen => !Gallery.ShowsPicture;

    /// <summary>
    /// Gets a value indicating whether the surface of a shown folder (a picture or the grid) covers the film.
    /// </summary>
    private bool ShowsGallerySurface => Gallery.IsActive && (Gallery.ShowsPicture || Gallery.IsGridOpen);

    /// <summary>
    /// Gets a value indicating whether the hint is shown instead of the video and the pictures.
    /// </summary>
    public bool ShowPlaceholder => !ShowVideoHost && !ShowPictures;

    /// <summary>
    /// Gets the folder strip, the common screen of pictures and looking at a picture alone.
    /// </summary>
    public GalleryViewModel Gallery { get; }

    /// <summary>
    /// Gets the hint shown instead of the video. The video area is also hidden while something is in progress
    /// (see <see cref="ShowVideo"/>): then the hint says what is being done, not that the video does not work.
    /// </summary>
    public string VideoPlaceholder
        => !HasMedia ? Texts.Get(IsHost ? "Main.Placeholder.ChooseFile" : "Main.Placeholder.HostNotChosen")
            : PlayerAvailable ? BusyText
            : Texts.Get("Main.Placeholder.NoVideo");

    /// <summary>
    /// Gets a value indicating whether the host can pick the first file from the placeholder.
    /// </summary>
    public bool CanPickFirstFile => IsHost && !HasMedia && !IsBusy && !Gallery.IsActive;

    /// <summary>
    /// Gets a value indicating whether the progress overlay of the start screen is shown.
    /// </summary>
    public bool ShowStartBusy => IsBusy && !IsInSession;

    /// <summary>
    /// Loads the stored user name.
    /// </summary>
    /// <returns>A task that completes when the settings are loaded.</returns>
    public async Task LoadAsync()
    {
        var name = await _settings.GetAsync(DISPLAY_NAME_SETTING, CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(name))
        {
            DisplayName = name;
        }

        OpenRouterPort = await _settings.GetAsync(OPEN_ROUTER_PORT_SETTING, CancellationToken.None) != "false";
        RendezvousServer = await _settings.GetAsync(RENDEZVOUS_SERVER_SETTING, CancellationToken.None) ?? string.Empty;

        // Before the choice existed, a typed address meant "use this server" and an empty one meant none; a device
        // that has an address keeps using it, everybody else gets the automatic search.
        RendezvousMode = Enum.TryParse<RendezvousMode>(await _settings.GetAsync(RENDEZVOUS_MODE_SETTING, CancellationToken.None), out var mode) &&
            Enum.IsDefined(mode)
                ? mode
                : RendezvousServer.Length > 0 ? RendezvousMode.Manual : RendezvousMode.Automatic;
        _session.RendezvousMode = RendezvousMode;
        if (_updates is not null)
        {
            _updates.ManifestUrl = await _settings.GetAsync(UpdateService.URL_SETTING, CancellationToken.None) ?? string.Empty;
            _updates.Enabled = await _settings.GetAsync(UpdateService.ENABLED_SETTING, CancellationToken.None) != "false";
            _ = CheckUpdatesAsync();
        }

        SideTab = await _settings.GetAsync(SIDE_TAB_SETTING, CancellationToken.None) == "events" ? SideTab.Events : SideTab.Chat;
        EventsExpanded = await _settings.GetAsync(EVENTS_EXPANDED_SETTING, CancellationToken.None) != "false";
        IsLayoutResizable = await _settings.GetAsync(LAYOUT_RESIZABLE_SETTING, CancellationToken.None) == "true";
        AlwaysShowBars = await _settings.GetAsync(ALWAYS_SHOW_BARS_SETTING, CancellationToken.None) == "true";
        if (_chatSound is not null)
        {
            await _chatSound.LoadAsync();
        }

        // A stored width means something only while the border can be dragged; a locked layout is the designed one.
        if (IsLayoutResizable &&
            double.TryParse(await _settings.GetAsync(SIDE_PANEL_WIDTH_SETTING, CancellationToken.None), NumberStyles.Float, CultureInfo.InvariantCulture, out var sideWidth) &&
            double.IsFinite(sideWidth))
        {
            SidePanelWidth = Math.Max(DEFAULT_SIDE_PANEL_WIDTH, sideWidth);
        }

        if (Volume is not null)
        {
            await Volume.LoadAsync();
        }

        if (Tracks is not null)
        {
            await Tracks.LoadAsync();
        }


        if (Devices is not null)
        {
            await Devices.RefreshAsync();
        }

        if (_avatarStore is not null)
        {
            _ownAvatar = await _avatarStore.LoadCurrentAsync();
        }
    }

    /// <summary>
    /// Starts hosting and shares a file, for example when the application was opened with a video file.
    /// </summary>
    /// <param name="path">The video file.</param>
    /// <returns>A task that completes when the file is shared.</returns>
    public Task HostFileAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return RunAsync(Texts.Get("Main.Busy.PreparingFile"), async token =>
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(Texts.Format("Main.Error.FileNotFound", path), path);
            }

            if (!_session.IsActive)
            {
                await SaveNameAsync();
                await _session.StartHostingAsync(Path.GetFileNameWithoutExtension(path), DisplayName, (int)(Port ?? TlsTransportOptions.DEFAULT_PORT), token);
                await SendStoredAvatarAsync(token);
            }

            await _session.ShareMediaAsync(path, token);
        });
    }

    /// <summary>
    /// Takes what the system handed to the application: the arguments of this start or of a later one that found
    /// this copy running. An invitation is offered to the user, a video file is shown.
    /// </summary>
    /// <param name="arguments">The command-line arguments; they are not trusted.</param>
    /// <returns>A task that completes when the arguments were dealt with.</returns>
    public async Task OpenExternalAsync(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.FirstOrDefault(Invite.LooksLikeLink) is { } link)
        {
            IsOpeningInvite = true;
            try
            {
                _dialogs.BringToFront();
                await OfferInviteAsync(link);
            }
            finally
            {
                IsOpeningInvite = false;
            }
        }
        else if (arguments.FirstOrDefault(a => !a.StartsWith('-') && File.Exists(a)) is { } file &&
            !IsBusy && (!_session.IsActive || _session.IsHost))
        {
            // "Open with Golether": host a session and show the file right away.
            _dialogs.BringToFront();
            await HostFileAsync(Path.GetFullPath(file));
        }
    }

    /// <summary>
    /// Offers to join the session of an invitation that came from outside (a link opened in the browser). Nothing
    /// is joined without a yes: any page can open such a link. In a session the user is asked whether to leave it.
    /// </summary>
    /// <param name="link">The invitation: the key or the web link.</param>
    /// <returns>A task that completes when the user declined or the join ended.</returns>
    public async Task OfferInviteAsync(string link)
    {
        if (_offeringInvite)
        {
            // One question at a time: a second link while the first is being answered is dropped.
            return;
        }

        _offeringInvite = true;
        try
        {
            Invite invite;
            try
            {
                invite = Invite.ParseLink(link);
            }
            catch (FormatException ex)
            {
                await _dialogs.ShowErrorAsync(Texts.Get("Main.Link.Bad.Title"), ex.Message);
                return;
            }

            if (IsBusy)
            {
                await _dialogs.ShowMessageAsync(Texts.Get("Main.Link.Busy.Title"), Texts.Get("Main.Link.Busy.Text"));
                return;
            }

            // The names come from the link, so they are what its author wrote; the verification code shown after
            // joining is what proves who the host is.
            var current = _session.IsActive ? _session.GetSnapshot()?.SessionName ?? string.Empty : null;
            var agreed = current is null
                ? await _dialogs.ConfirmAsync(
                    Texts.Get("Main.Link.Join.Title"),
                    Texts.Format("Main.Link.Join.Text", invite.SessionName, invite.HostName),
                    Texts.Get("Main.Link.Join.Accept"))
                : await _dialogs.ConfirmAsync(
                    Texts.Get("Main.Link.Switch.Title"),
                    Texts.Format("Main.Link.Switch.Text", current, invite.SessionName, invite.HostName),
                    Texts.Get("Main.Link.Switch.Accept"));
            if (!agreed || IsBusy)
            {
                return;
            }

            if (_session.IsActive)
            {
                await LeaveAsync();
            }

            InviteLink = link.Trim();
            await JoinAsync();
        }
        finally
        {
            _offeringInvite = false;
        }
    }

    /// <summary>
    /// Sets the state of the player backend.
    /// </summary>
    /// <param name="available">Whether libmpv renders the video.</param>
    /// <param name="notice">The notice, or <see langword="null"/>.</param>
    public void SetPlayerState(bool available, string? notice)
    {
        PlayerAvailable = available;
        PlayerNotice = notice ?? string.Empty;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(PlayerAvailable) or nameof(HasMedia) or nameof(IsAwaitingApproval) or nameof(IsBusy) or nameof(BusyText) or nameof(IsHost)
                or nameof(IsInSession):
                base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(ShowVideo)));
                base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(ShowVideoHost)));
                base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(ShowPictures)));
                base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(ShowVideoOverlay)));
                base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(ShowPlaceholder)));
                base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(VideoPlaceholder)));
                base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(CanPickFirstFile)));
                base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(ShowStartBusy)));
                break;
        }
    }

    /// <summary>
    /// Updates the view from the session snapshot; called by a timer and on session changes.
    /// </summary>
    public void Refresh()
    {
        Interlocked.Exchange(ref _refreshQueued, 0);
        var snapshot = _session.GetSnapshot();
        ReconnectText = _session.ReconnectMessage;
        IsInSession = (snapshot is not null && snapshot.State != SessionState.Ended) || IsReconnecting;
        if (snapshot is null)
        {
            Gallery.Update(null, Participants);
            Participants.Clear();
            _peerAvatars.Clear();
            IsHost = false;
            IsAwaitingApproval = false;
            IsWaitingForHost = false;
            _resumeMedia = null;
            ResumeOffer = null;
            if (Chat.HasLines || Chat.Reactions.Count > 0)
            {
                Chat.Reset();
            }

            FirewallBlocked = false;
            Drawing.CanDraw = false;
            if (Drawing.Strokes.Count > 0)
            {
                Drawing.Reset();
            }

            return;
        }

        IsHost = snapshot.IsHost;
        SessionTitle = snapshot.SessionName;
        IsAwaitingApproval = snapshot.State is SessionState.Connecting or SessionState.AwaitingApproval;
        IsWaitingForHost = snapshot.State == SessionState.AwaitingApproval;
        VerificationText = snapshot.VerificationCode?.ToString() ?? string.Empty;
        HasMedia = snapshot.Media is not null;
        MediaCaption = snapshot.Media is { } media
            ? $"{media.FileName} · {DisplayFormat.Size(media.Length)}{(snapshot.UsesLocalCopy ? " · " + Texts.Get("Main.Caption.LocalCopy") : string.Empty)}"
            : Texts.Get(snapshot.IsHost ? "Main.Caption.ChooseFile" : "Main.Placeholder.HostNotChosen");

        var local = snapshot.Local;
        var duration = local?.Snapshot.Duration;
        DurationSeconds = Math.Max(1, duration?.TotalSeconds ?? 1);
        DurationText = DisplayFormat.Position(duration);
        var position = local?.Snapshot.Position ?? local?.Expected;
        var seeking = local?.Snapshot is { IsLoaded: true, IsBuffering: true };
        if (!IsScrubbing)
        {
            // While the player seeks or waits for data its own position is stale: the handle shows where the
            // session is instead. Left where it was, it would disagree with the mark of this participant on the
            // same timeline for as long as the data takes to arrive, which on a slow connection is a long time.
            // Until the film is opened its length is unknown: the handle stays at the start instead of being
            // pushed to the end of an empty timeline, which looks as if the film were over.
            var shown = seeking ? local?.Expected ?? position : position;
            PositionSeconds = duration is { Ticks: > 0 } ? Math.Clamp(shown?.TotalSeconds ?? 0, 0, DurationSeconds) : 0;
        }

        PositionText = DisplayFormat.Position(position);
        IsPlaying = snapshot.Playback?.State == PlayState.Playing;
        IsAdvancing = IsPlaying && local?.StartsIn is null && local?.Snapshot is { IsPaused: false, IsBuffering: false };
        IsEnded = snapshot.Playback is { State: PlayState.Paused, Cause: PlaybackCause.Ended };
        IsWaiting = snapshot.Playback is { State: PlayState.Paused, Cause: PlaybackCause.WaitingForParticipants };
        var seconds = !IsWaiting && local?.StartsIn is { } startsIn ? Math.Ceiling(startsIn.TotalSeconds).ToString("0", CultureInfo.InvariantCulture) : null;
        CountdownText = IsWaiting
            ? DescribeWaiting(snapshot, duration)
            : seconds is not null ? Texts.Format("Main.StartsIn", seconds) : string.Empty;
        CountdownShort = seconds ?? string.Empty;
        UpdateResume(snapshot, position, duration);
        var buffered = ComputeBufferedRanges(snapshot, duration);
        if (!buffered.SequenceEqual(BufferedRanges))
        {
            BufferedRanges = buffered;
        }
        Chat.Tick();
        FirewallBlocked = _session.FirewallBlocked;
        UpdateParticipants(snapshot, duration);
        Gallery.Update(snapshot, Participants);

        // Over a shown folder the top bar names the item on the screen with its nested folder; a video of the folder,
        // which is the film then, keeps its size like a film.
        if (Gallery.IsActive)
        {
            MediaCaption = Gallery.ShowsPicture || snapshot.Media is not { } film
                ? Gallery.ShownName
                : $"{Gallery.ShownName} · {DisplayFormat.Size(film.Length)}";
        }

        // Over the pictures the strokes follow the picture, and only over the one everybody sees.
        Drawing.CanDraw = snapshot.State == SessionState.Active && (!Gallery.IsActive || Gallery.CanDraw);
        Drawing.Tick();
        VideoAspect = Gallery.ShowsPicture ? Gallery.PictureAspect : _playerControls?.GetVideoAspect() ?? 0;

        UpdateSyncChip(snapshot);
        SecurityText = Texts.Format("Main.Security", Participants.Count, SessionOptions.MAX_PARTICIPANTS_LIMIT);
        var own = snapshot.Participants.FirstOrDefault(p => p.IsLocal)?.Status;
        SharingText = own is { BytesFromPeers: > 0 } or { BytesToPeers: > 0 }
            ? Texts.Format("Main.Sharing", DisplayFormat.Size(own.BytesFromPeers), DisplayFormat.Size(own.BytesToPeers))
            : string.Empty;
        ClockText = snapshot.IsHost
            ? Texts.Get("Main.Clock.Host")
            : snapshot.ClockUncertainty is { } uncertainty
                ? Texts.Format("Main.Clock.Participant", ((int)uncertainty.TotalMilliseconds).ToString(CultureInfo.InvariantCulture), DisplayFormat.Ping((int?)snapshot.RoundTrip?.TotalMilliseconds))
                : Texts.Get("Main.Clock.Syncing");
    }

    /// <summary>
    /// Gets or sets the stored position of the current film the host may continue from.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResumeOffer), nameof(ResumeText))]
    public partial TimeSpan? ResumeOffer { get; set; }

    /// <summary>
    /// Gets a value indicating whether continuing is offered.
    /// </summary>
    public bool HasResumeOffer => ResumeOffer is not null;

    /// <summary>
    /// Gets the text of the continue offer.
    /// </summary>
    public string ResumeText => ResumeOffer is { } offer ? Texts.Format("Main.ResumeOffer", DisplayFormat.Position(offer)) : string.Empty;

    /// <summary>
    /// Remembers the position of the current film and offers the host to continue a film stopped earlier.
    /// </summary>
    /// <param name="snapshot">The session.</param>
    /// <param name="position">The local position.</param>
    /// <param name="duration">The duration.</param>
    private void UpdateResume(SessionSnapshot snapshot, TimeSpan? position, TimeSpan? duration)
    {
        if (snapshot.Media is not { } media)
        {
            _resumeMedia = null;
            ResumeOffer = null;
            return;
        }

        if (duration is null)
        {
            return;
        }

        var id = media.QuickId;
        if (id != _resumeMedia)
        {
            _resumeMedia = id;
            ResumeOffer = null;
            _ = BeginResumeAsync(id, duration, snapshot.IsHost);
            return;
        }

        if (position is { } current)
        {
            _resume.Report(id, current, duration, IsPlaying, _timeProvider.GetUtcNow());
            if (current >= ResumeTracker.MinimumPosition)
            {
                ResumeOffer = null;
            }
        }
    }

    /// <summary>
    /// Reads the stored position of a film.
    /// </summary>
    /// <param name="mediaId">The media.</param>
    /// <param name="duration">The duration.</param>
    /// <param name="host">Whether this device leads the session.</param>
    /// <returns>A task that completes when the offer is shown.</returns>
    private async Task BeginResumeAsync(string mediaId, TimeSpan? duration, bool host)
    {
        var offer = await _resume.BeginAsync(mediaId, duration);
        if (host && _resumeMedia == mediaId && PositionSeconds < ResumeTracker.MinimumPosition.TotalSeconds)
        {
            ResumeOffer = offer;
        }
    }

    /// <summary>
    /// Continues the film for everybody from the stored position.
    /// </summary>
    /// <returns>A task that completes when the request is sent.</returns>
    [RelayCommand]
    private Task ResumeAsync()
    {
        if (ResumeOffer is not { } offer)
        {
            return Task.CompletedTask;
        }

        ResumeOffer = null;
        return SeekToAsync(offer.TotalSeconds);
    }

    /// <summary>
    /// Hides the continue offer.
    /// </summary>
    [RelayCommand]
    private void DismissResume() => ResumeOffer = null;

    /// <summary>
    /// Gets a value indicating whether a diagnostic report can be saved.
    /// </summary>
    public bool CanSaveReport => _diagnostics is not null;

    /// <summary>
    /// Gets or sets the offer to update, or an empty string.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    public partial string UpdateText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets what the new version brings.
    /// </summary>
    [ObservableProperty]
    public partial string UpdateNotes { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the offered version is not to be offered again once the offer is
    /// closed.
    /// </summary>
    [ObservableProperty]
    public partial bool IgnoreUpdate { get; set; }

    /// <summary>
    /// Gets a value indicating whether a newer version is offered.
    /// </summary>
    public bool HasUpdate => UpdateText.Length > 0;

    /// <summary>
    /// Gets a value indicating whether new versions are looked for.
    /// </summary>
    public bool ChecksUpdates => _updates?.Enabled == true;

    /// <summary>
    /// Gets the address of the update source typed by the user, or an empty string for the project page.
    /// </summary>
    public string UpdateUrl => _updates?.ManifestUrl ?? string.Empty;

    /// <summary>
    /// Stores the choices of the settings window about updates and looks for a new version at once.
    /// </summary>
    /// <param name="enabled">Whether new versions are looked for.</param>
    /// <param name="url">The address of the update source, or an empty string for the project page.</param>
    public void SetUpdateSource(bool enabled, string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (_updates is null)
        {
            return;
        }

        _updates.Enabled = enabled;
        _updates.ManifestUrl = url.Trim();
        _ = _settings.SetAsync(UpdateService.ENABLED_SETTING, enabled ? "true" : "false", CancellationToken.None);
        _ = _settings.SetAsync(UpdateService.URL_SETTING, _updates.ManifestUrl, CancellationToken.None);
        _ = CheckUpdatesAsync();
    }

    /// <summary>
    /// Asks the update source whether a newer version exists. A version the user chose to ignore is not offered.
    /// </summary>
    /// <returns>A task that completes when the answer is shown.</returns>
    public async Task CheckUpdatesAsync()
    {
        if (_updates is not { } updates)
        {
            return;
        }

        var found = await updates.CheckAsync(CancellationToken.None);
        if (found is not null && found.Version == await _settings.GetAsync(UpdateService.IGNORED_VERSION_SETTING, CancellationToken.None))
        {
            found = null;
        }

        _update = found;
        IgnoreUpdate = false;
        UpdateText = found is null ? string.Empty : Texts.Format("Main.Update.Available", found.Version);
        UpdateNotes = found?.Notes ?? string.Empty;
    }

    /// <summary>
    /// Opens the page with the downloads of the offered version in the browser. Nothing is installed by itself.
    /// </summary>
    /// <returns>A task that completes when the browser was asked.</returns>
    [RelayCommand]
    private async Task OpenUpdatePageAsync()
    {
        if (_update is { } update)
        {
            await _dialogs.OpenLinkAsync(UpdateService.GetPage(update));
        }
    }

    /// <summary>
    /// Hides the offer until the next start, or for good when the user chose to ignore this version.
    /// </summary>
    [RelayCommand]
    private void DismissUpdate()
    {
        if (IgnoreUpdate && _update is { } update)
        {
            _ = _settings.SetAsync(UpdateService.IGNORED_VERSION_SETTING, update.Version, CancellationToken.None);
        }

        UpdateText = string.Empty;
    }

    /// <summary>
    /// Saves a report about this device and the session: what is installed, how the session goes and the tail of the
    /// log. Keys and tokens are cut out of it.
    /// </summary>
    /// <returns>A task that completes when the report is saved.</returns>
    [RelayCommand]
    private async Task SaveReportAsync()
    {
        if (_diagnostics is not { } diagnostics)
        {
            return;
        }

        try
        {
            var notes = new List<string>
            {
                Texts.Format("Main.Report.Player", PlayerNotice.Length > 0 ? PlayerNotice : Texts.Get("Main.Report.Works")),
                Texts.Format("Main.Report.Conference", ConferenceAvailable ? Texts.Get("Main.Report.WorkPlural") : ConferenceNotice),
                Texts.Format(
                    "Main.Report.Devices",
                    Texts.Get(MicrophoneMuted ? "Main.Report.MicrophoneOff" : "Main.Report.MicrophoneOn"),
                    Texts.Get(CameraOff ? "Main.Report.CameraOff" : "Main.Report.CameraOn")),
                Texts.Format("Main.Report.Sync", SyncText),
            };
            if (ReconnectText.Length > 0)
            {
                notes.Add(Texts.Format("Main.Report.Reconnect", ReconnectText));
            }

            // The frame counters show where the link breaks: the connection did not come up, came up empty, or the
            // camera gives nothing.
            var links = _conference.GetPeerDiagnostics();
            notes.Add(links.Count == 0 ? Texts.Get("Main.Report.NoLinks") : Texts.Format("Main.Report.Links", links.Count));
            foreach (var link in links)
            {
                notes.Add(Texts.Format(
                    "Main.Report.Link",
                    link.Peer.ToShortString(),
                    Texts.Get(link.Verified ? "Main.Report.Verified" : "Main.Report.NotVerified"),
                    Texts.Get(link.DataReady ? "Main.Report.ChannelOpen" : "Main.Report.ChannelClosed"),
                    link.Quality,
                    link.VideoSent,
                    link.VideoReceived,
                    link.AudioSent,
                    link.AudioReceived));
            }

            var path = await diagnostics.SaveAsync(_session.GetSnapshot(), notes);
            await _dialogs.ShowMessageAsync(Texts.Get("Main.Report.Ready.Title"), Texts.Format("Main.Report.Ready.Text", path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(Texts.Get("Main.Report.Failed.Title"), ex.Message);
        }
    }

    /// <summary>
    /// Starts hosting a session.
    /// </summary>
    /// <returns>A task that completes when the session runs.</returns>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartHostingAsync() => RunAsync(Texts.Get("Main.Busy.CreatingSession"), async token =>
    {
        await SaveNameAsync();
        await _session.StartHostingAsync(NewSessionName, DisplayName, (int)(Port ?? TlsTransportOptions.DEFAULT_PORT), token);
        await SendStoredAvatarAsync(token);
    });

    /// <summary>
    /// Joins a session. The wait for the host can be cancelled with <see cref="CancelJoinCommand"/>.
    /// </summary>
    /// <returns>A task that completes when the host admitted this device or the user cancelled.</returns>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task JoinAsync()
    {
        using var cancellation = new CancellationTokenSource();
        _joinCancellation = cancellation;
        IsCancellable = true;
        try
        {
            await RunAsync(Texts.Get("Main.Busy.Connecting"), async token =>
            {
                await SaveNameAsync();
                await _session.JoinAsync(InviteLink, DisplayName, token);
                await SendStoredAvatarAsync(token);
                InviteLink = string.Empty;
            }, cancellation.Token);
        }
        finally
        {
            IsCancellable = false;
            _joinCancellation = null;
        }
    }

    /// <summary>
    /// Stops a join that is still waiting for the host to admit this device.
    /// </summary>
    [RelayCommand]
    private void CancelJoin() => _joinCancellation?.Cancel();

    /// <summary>
    /// Pastes an invitation from the clipboard.
    /// </summary>
    /// <returns>A task that completes when the text is pasted.</returns>
    [RelayCommand]
    private async Task PasteInviteAsync() => InviteLink = (await _dialogs.PasteTextAsync())?.Trim() ?? InviteLink;

    /// <summary>
    /// Shares a media file (host) or selects a local copy (participant).
    /// </summary>
    /// <returns>A task that completes when the file is used.</returns>
    [RelayCommand]
    private async Task PickFileAsync()
    {
        var path = await _dialogs.PickMediaFileAsync();
        if (path is null)
        {
            return;
        }

        await RunAsync(Texts.Get("Main.Busy.PreparingFile"), async token =>
        {
            if (_session.IsHost && GalleryFolder.IsPicture(path))
            {
                // One picture is a showing of one: the host chose exactly this file, so nothing else of its folder
                // goes out, and its metadata stays at home.
                await _session.ShowGalleryAsync(GalleryFolder.Describe(path, canDecode: _session.CanDecodePictures), stripMetadata: true, progress: null, token);
            }
            else if (_session.IsHost)
            {
                await _session.ShareMediaAsync(path, token);
            }
            else if (!await _session.UseLocalCopyAsync(path, token))
            {
                await _dialogs.ShowErrorAsync(Texts.Get("Main.Error.OtherFile.Title"), Texts.Get("Main.Error.OtherFile.Text"));
            }
        });
    }

    /// <summary>
    /// Shows a folder (host): the browser shows what lies in the folders and counts what would go out, the host
    /// confirms it and whether the metadata of the pictures stays at home, and only then is anything read or sent.
    /// </summary>
    /// <returns>A task that completes when the folder is shown or the host changed their mind.</returns>
    [RelayCommand]
    private async Task PickFolderAsync()
    {
        if (!_session.IsHost)
        {
            return;
        }

        _lastGalleryFolder ??= await _settings.GetAsync(GALLERY_FOLDER_SETTING, CancellationToken.None);
        using var browser = new FolderBrowserViewModel(_pictures, _session.CanDecodePictures, _lastGalleryFolder);
        var choice = await _dialogs.ChooseGalleryAsync(browser);

        // The browser opens next time where it was left, chosen or not, also after a restart.
        if (browser.CurrentPath.Length > 0 && !string.Equals(browser.CurrentPath, _lastGalleryFolder, StringComparison.Ordinal))
        {
            _lastGalleryFolder = browser.CurrentPath;
            await _settings.SetAsync(GALLERY_FOLDER_SETTING, _lastGalleryFolder, CancellationToken.None);
        }

        if (choice is null)
        {
            return;
        }
        var total = choice.Folder.Files.Count;
        var progress = new Progress<int>(done =>
        {
            if (IsBusy)
            {
                BusyText = Texts.Format("Main.Busy.PreparingPicturesProgress", done, total);
            }
        });
        await RunAsync(Texts.Get("Main.Busy.PreparingPictures"), token => _session.ShowGalleryAsync(choice.Folder, choice.StripMetadata, progress, token));
    }

    /// <summary>
    /// Starts or pauses playback for everybody.
    /// </summary>
    /// <returns>A task that completes when the intent was sent.</returns>
    [RelayCommand]
    private Task TogglePlayAsync()
        => SendAsync(IsEnded
            ? new PlaybackRequest(PlaybackRequestKind.Play, TimeSpan.Zero)
            : IsWaiting
                ? new PlaybackRequest(PlaybackRequestKind.Pause, TimeSpan.FromSeconds(PositionSeconds))
                : new PlaybackRequest(IsPlaying ? PlaybackRequestKind.Pause : PlaybackRequestKind.Play, IsPlaying ? TimeSpan.FromSeconds(PositionSeconds) : null));

    /// <summary>
    /// Starts playback for everybody without waiting for participants that are not ready (host).
    /// </summary>
    /// <returns>A task that completes when the intent was sent.</returns>
    [RelayCommand]
    private Task StartNowAsync() => SendAsync(new PlaybackRequest(PlaybackRequestKind.PlayNow, null));

    /// <summary>
    /// Names the participants playback waits for.
    /// </summary>
    /// <param name="snapshot">The session.</param>
    /// <param name="duration">The media duration.</param>
    /// <returns>The text, for example <c>Waiting for: Marina and Oleg…</c>.</returns>
    internal static string DescribeWaiting(SessionSnapshot snapshot, TimeSpan? duration)
    {
        var target = snapshot.Playback?.Position ?? TimeSpan.Zero;
        var names = snapshot.Participants
            .Where(p => !p.Info.IsHost && (p.Status is not { } status ||
                !_readinessOptions.IsReady(status, duration) ||
                status.Position is not { } position ||
                (position - target).Duration() > _readinessOptions.WaitThreshold))
            .Select(p => p.Info.DisplayName)
            .ToArray();
        return names.Length switch
        {
            0 => Texts.Get("Main.Waiting.Everybody"),
            1 => Texts.Format("Main.Waiting.One", names[0]),
            2 => Texts.Format("Main.Waiting.Two", names[0], names[1]),
            _ => Texts.Format("Main.Waiting.Many", names[0], names[1], names.Length - 2),
        };
    }

    /// <summary>
    /// Seeks back by ten seconds.
    /// </summary>
    /// <returns>A task that completes when the intent was sent.</returns>
    [RelayCommand]
    private Task SeekBackAsync() => SeekToAsync(Math.Max(0, PositionSeconds - _seekStep.TotalSeconds));

    /// <summary>
    /// Seeks forward by ten seconds.
    /// </summary>
    /// <returns>A task that completes when the intent was sent.</returns>
    [RelayCommand]
    private Task SeekForwardAsync() => SeekToAsync(Math.Min(DurationSeconds, PositionSeconds + _seekStep.TotalSeconds));

    /// <summary>
    /// Seeks to a position in seconds.
    /// </summary>
    /// <param name="seconds">The position.</param>
    /// <returns>A task that completes when the intent was sent.</returns>
    [RelayCommand]
    private Task SeekToAsync(double seconds)
    {
        PositionSeconds = seconds;
        return SendAsync(new PlaybackRequest(PlaybackRequestKind.Seek, TimeSpan.FromSeconds(seconds)));
    }

    /// <summary>
    /// Shows the invitation dialog.
    /// </summary>
    /// <returns>A task that completes when the dialog is closed.</returns>
    [RelayCommand]
    private async Task InviteAsync()
    {
        var dialog = new InviteDialogViewModel(_session, _dialogs);
        dialog.Regenerate();
        await _dialogs.ShowInviteAsync(dialog);
    }

    /// <summary>
    /// Shows the tunnel dialog.
    /// </summary>
    /// <returns>A task that completes when the dialog is closed.</returns>
    [RelayCommand]
    private async Task OpenTunnelsAsync()
    {
        var dialog = _tunnelDialogFactory(DisplayName);
        await dialog.InitializeAsync();
        await _dialogs.ShowTunnelsAsync(dialog);
    }

    /// <summary>
    /// Leaves or ends the session.
    /// </summary>
    /// <returns>A task that completes when the session is closed.</returns>
    [RelayCommand]
    private async Task LeaveAsync()
    {
        // A join may still be waiting for the host; cancel it so its progress does not linger.
        _joinCancellation?.Cancel();
        await _session.LeaveAsync();
        Events.Clear();
        LastEvent = string.Empty;
        Refresh();
    }

    /// <summary>
    /// Determines whether a session can be started.
    /// </summary>
    /// <returns><see langword="true"/> when idle.</returns>
    private bool CanStart() => !IsBusy;

    /// <summary>
    /// Sends a playback intent and reports failures.
    /// </summary>
    /// <param name="request">The intent.</param>
    /// <returns>A task that completes when the intent was sent.</returns>
    private async Task SendAsync(PlaybackRequest request)
    {
        try
        {
            await _session.RequestAsync(request, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // This is where a command of the user ends: whatever failed below (the connection, the player), it is
            // shown. An exception that leaves a command would end the whole application.
            await _dialogs.ShowErrorAsync(Texts.Get("Main.Error.CommandNotSent"), ex.Message);
        }
    }

    /// <summary>
    /// Runs an operation with the busy indicator and shows errors.
    /// </summary>
    /// <param name="text">The progress text.</param>
    /// <param name="operation">The operation.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the operation finished.</returns>
    private async Task RunAsync(string text, Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        BusyText = text;
        try
        {
            await operation(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The user stopped the operation (for example a join waiting for the host); nothing to report.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // This is where a command of the user ends: a failure of any kind (a bad link, the network, the player,
            // the disk) is shown instead of ending the whole application.
            await _dialogs.ShowErrorAsync(Texts.Get("Main.Error.Failed"), ex.Message);
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
            Refresh();
        }
    }

    /// <summary>
    /// Stores the user name.
    /// </summary>
    /// <returns>A task that completes when the name is saved.</returns>
    private async Task SaveNameAsync()
    {
        DisplayName = Core.Session.ParticipantInfo.NormalizeDisplayName(DisplayName);
        await _settings.SetAsync(DISPLAY_NAME_SETTING, DisplayName, CancellationToken.None);
    }

    /// <summary>
    /// Queues a refresh on the UI thread, coalescing bursts of changes.
    /// </summary>
    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 0)
        {
            _dispatcher.Post(Refresh);
        }
    }

    /// <summary>
    /// Adds an event feed entry.
    /// </summary>
    /// <param name="sessionEvent">The event.</param>
    private void AddEvent(SessionEvent sessionEvent)
    {
        var text = $"{sessionEvent.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}  {sessionEvent.Text}";
        Events.Insert(0, text);
        while (Events.Count > MAX_EVENTS)
        {
            Events.RemoveAt(Events.Count - 1);
        }

        LastEvent = sessionEvent.Text;
    }

    /// <summary>
    /// Synchronizes the participant tiles with the snapshot.
    /// </summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <param name="duration">The media duration.</param>
    private void UpdateParticipants(SessionSnapshot snapshot, TimeSpan? duration)
    {
        var views = snapshot.Participants;
        for (var i = Participants.Count - 1; i >= 0; i--)
        {
            if (views.All(v => v.Info.PeerId != Participants[i].PeerId))
            {
                Participants.RemoveAt(i);
            }
        }

        for (var i = 0; i < views.Count; i++)
        {
            var item = Participants.FirstOrDefault(p => p.PeerId == views[i].Info.PeerId);
            if (item is null)
            {
                item = new ParticipantItemViewModel(views[i].Info.PeerId, SwitchOffParticipantDevicesAsync, ChangeVoiceVolume, RemoveParticipantAsync);
                Participants.Insert(_participantsReordered ? Participants.Count : Math.Min(i, Participants.Count), item);
                if (!views[i].IsLocal)
                {
                    _ = RestoreVoiceVolumeAsync(item);
                }
            }

            item.Update(views[i], duration);
            item.AvatarImage = _peerAvatars.GetValueOrDefault(item.PeerId);
            item.CanModerate = IsHost && !item.IsLocal;
        }
    }

    /// <summary>
    /// Applies the local volume of a participant's voice and remembers it for later sessions.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="percent">The volume in percent.</param>
    private void ChangeVoiceVolume(PeerId peer, double percent)
    {
        _conference.SetVoiceVolume(peer, percent / 100);
        var version = Interlocked.Increment(ref _voiceVolumeVersion);
        _ = SaveVoiceVolumeLaterAsync(peer, percent, version);
    }

    /// <summary>
    /// Stores a voice volume once the slider has stopped.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="percent">The volume in percent.</param>
    /// <param name="version">The change number; newer changes replace this one.</param>
    /// <returns>A task that completes when the value is stored.</returns>
    private async Task SaveVoiceVolumeLaterAsync(PeerId peer, double percent, long version)
    {
        await Task.Delay(VoiceVolumeSaveDelay).ConfigureAwait(false);
        if (Interlocked.Read(ref _voiceVolumeVersion) == version)
        {
            await _settings.SetAsync(VOICE_VOLUME_SETTING_PREFIX + peer.Value, percent.ToString(CultureInfo.InvariantCulture), CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Applies the stored voice volume of a participant.
    /// </summary>
    /// <param name="item">The participant tile.</param>
    /// <returns>A task that completes when the volume is applied.</returns>
    private async Task RestoreVoiceVolumeAsync(ParticipantItemViewModel item)
    {
        var stored = await _settings.GetAsync(VOICE_VOLUME_SETTING_PREFIX + item.PeerId.Value, CancellationToken.None);
        if (double.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) && percent != item.VoiceVolume)
        {
            item.RestoreVoiceVolume(percent);
            _conference.SetVoiceVolume(item.PeerId, item.VoiceVolume / 100);
        }
    }

    /// <summary>
    /// Marks the tile of a participant that started or stopped speaking; a muted microphone never speaks.
    /// </summary>
    /// <param name="change">The change.</param>
    private void ShowSpeaking(SpeakingChange change)
    {
        var tile = change.PeerId.IsEmpty
            ? Participants.FirstOrDefault(p => p.IsLocal)
            : Participants.FirstOrDefault(p => p.PeerId == change.PeerId);
        if (tile is not null)
        {
            tile.IsSpeaking = change.IsSpeaking && !(tile.IsLocal ? MicrophoneMuted : tile.MicrophoneOff);
        }
    }

    /// <summary>
    /// Removes a participant from the session (host only): asks the host first, since the participant will need a
    /// new invitation to come back, and reports failures.
    /// </summary>
    /// <param name="peerId">The participant.</param>
    /// <returns>A task that completes when the participant was removed or the host changed their mind.</returns>
    private async Task RemoveParticipantAsync(Core.Identity.PeerId peerId)
    {
        var name = Participants.FirstOrDefault(p => p.PeerId == peerId)?.Name ?? string.Empty;
        if (!await _dialogs.ConfirmAsync(
            Texts.Get("Main.Moderation.Remove.Title"),
            Texts.Format("Main.Moderation.Remove.Text", name),
            Texts.Get("Main.Moderation.Remove.Accept")))
        {
            return;
        }

        try
        {
            await _session.RemoveParticipantAsync(peerId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            await _dialogs.ShowErrorAsync(Texts.Get("Main.Error.RemoveFailed"), ex.Message);
        }
    }

    /// <summary>
    /// Switches devices of a participant off (host only) and reports failures.
    /// </summary>
    /// <param name="peerId">The participant.</param>
    /// <param name="microphone">Whether to switch the microphone off.</param>
    /// <param name="camera">Whether to switch the camera off.</param>
    /// <returns>A task that completes when the request was sent.</returns>
    private async Task SwitchOffParticipantDevicesAsync(Core.Identity.PeerId peerId, bool microphone, bool camera)
    {
        try
        {
            await _session.SwitchOffParticipantDevicesAsync(peerId, microphone, camera);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            await _dialogs.ShowErrorAsync(Texts.Get("Main.Error.SwitchOffFailed"), ex.Message);
        }
    }

    /// <summary>
    /// Updates the synchronization chip.
    /// </summary>
    /// <param name="snapshot">The snapshot.</param>
    private void UpdateSyncChip(SessionSnapshot snapshot)
    {
        if (IsAwaitingApproval)
        {
            SyncText = Texts.Get("Main.Sync.WaitingHost");
            SyncLevel = IndicatorLevel.Warning;
            return;
        }

        var drifts = snapshot.Participants.Select(p => p.Status).OfType<Core.Session.ParticipantStatus>().Where(s => s.Position is not null).ToArray();
        if (drifts.Length == 0 || snapshot.Playback?.State != PlayState.Playing)
        {
            SyncText = Texts.Get(snapshot.Playback?.State == PlayState.Playing ? "Main.Sync.Syncing" : IsEnded ? "Main.Sync.Ended" : "Main.Sync.Paused");
            SyncLevel = IndicatorLevel.Neutral;
            return;
        }

        var worst = drifts.Max(s => s.Drift.Duration());
        SyncText = Texts.Format("Main.Sync.InSync", Texts.Format("Format.Milliseconds", (int)worst.TotalMilliseconds));
        SyncLevel = DisplayFormat.DriftLevel(worst);
        if (drifts.Any(s => s.IsBuffering))
        {
            SyncText = Texts.Get("Main.Sync.SomeoneBuffering");
            SyncLevel = IndicatorLevel.Warning;
        }
    }
}
