using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Components.Catalog;
using Golether.Core.Data.Stores;
using Golether.Core.Identity;
using Golether.Core.Playback;
using Golether.Media.Conference;
using Golether.Media.Player;
using Golether.Session;
using Golether.Sync.Protocol;
using Golether.Transports.Tls;
using Golether.UI.Services;
using Golether.UI.ViewModels.Dialogs;

namespace Golether.UI.ViewModels;

/// <summary>
/// The tabs of the side panel.
/// </summary>
public enum SideTab
{
    /// <summary>
    /// The chat.
    /// </summary>
    Chat = 0,

    /// <summary>
    /// The event feed.
    /// </summary>
    Events = 1,
}

/// <summary>
/// The main window: the start screen and the running session.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    /// <summary>
    /// The setting with the user name.
    /// </summary>
    public const string DisplayNameSetting = "displayName";

    /// <summary>
    /// The setting that keeps the event feed shown or hidden.
    /// </summary>
    public const string EventsExpandedSetting = "ui.eventsExpanded";

    /// <summary>
    /// The setting with the open tab of the side panel (<c>chat</c> or <c>events</c>).
    /// </summary>
    public const string SideTabSetting = "ui.sideTab";

    /// <summary>
    /// The prefix of the settings with the local voice volume of a participant (by device identifier).
    /// </summary>
    public const string VoiceVolumeSettingPrefix = "voice.volume.";

    /// <summary>
    /// How long a voice volume change waits before it is stored.
    /// </summary>
    internal static TimeSpan VoiceVolumeSaveDelay { get; set; } = TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// The setting that allows opening the port on the router (<c>true</c> or <c>false</c>).
    /// </summary>
    public const string OpenRouterPortSetting = "network.openRouterPort";

    /// <summary>
    /// The maximum number of event feed entries.
    /// </summary>
    private const int MaxEvents = 50;

    /// <summary>
    /// The seek step of the back/forward buttons.
    /// </summary>
    private static readonly TimeSpan SeekStep = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The readiness rules of the host, used to name the participants playback waits for.
    /// </summary>
    private static readonly Golether.Sync.Engine.PlaybackAuthorityOptions ReadinessOptions = new();

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
    /// Whether a refresh is already queued.
    /// </summary>
    private int _refreshQueued;

    /// <summary>
    /// Remembers where films were stopped.
    /// </summary>
    private readonly ResumeTracker _resume;

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
    /// The folder for downloaded updates.
    /// </summary>
    private readonly string _updateFolder;

    /// <summary>
    /// The release offered to the user, or <see langword="null"/>.
    /// </summary>
    private UpdateInfo? _update;

    /// <summary>
    /// Counts voice volume changes, so only the last one is stored.
    /// </summary>
    private long _voiceVolumeVersion;

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
    /// <param name="updateFolder">The folder for downloaded updates.</param>
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
        string? updateFolder = null)
    {
        ArgumentNullException.ThrowIfNull(components);
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
        _updateFolder = updateFolder ?? Path.Combine(Path.GetTempPath(), "golether-updates");
        Chat = new ChatViewModel(_session, _dispatcher);
        Drawing = new DrawingViewModel(_session, _dispatcher);
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
                await _dialogs.ShowMessageAsync(
                    "Правило не добавлено",
                    "Без него участники не подключатся. Добавить можно и вручную: «Брандмауэр Защитника Windows» → «Разрешить взаимодействие с приложением» → Golether, галочки для частной и общедоступной сети.");
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
    [NotifyPropertyChangedFor(nameof(ShowChat), nameof(ShowEvents), nameof(SidePanelArrow))]
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
    /// Gets the camera and microphone choice, or <see langword="null"/> when the backend cannot list devices.
    /// </summary>
    public DevicesViewModel? Devices { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the microphone is muted.
    /// </summary>
    [ObservableProperty]
    public partial bool MicrophoneMuted { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the camera is off.
    /// </summary>
    [ObservableProperty]
    public partial bool CameraOff { get; set; }

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
        _ = _settings.SetAsync(EventsExpandedSetting, EventsExpanded ? "true" : "false", CancellationToken.None);
        _ = _settings.SetAsync(SideTabSetting, SideTab == SideTab.Events ? "events" : "chat", CancellationToken.None);
    }

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
    public partial string NewSessionName { get; set; } = "Вечер кино";

    /// <summary>
    /// Gets or sets the listening port of a new session.
    /// </summary>
    [ObservableProperty]
    public partial decimal? Port { get; set; } = TlsTransportOptions.DefaultPort;

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
        _ = _settings.SetAsync(OpenRouterPortSetting, value ? "true" : "false", CancellationToken.None);
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
    /// Gets or sets a value indicating whether a session runs.
    /// </summary>
    [ObservableProperty]
    public partial bool IsInSession { get; set; }

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
    public string PlayButtonText => IsEnded ? "Смотреть сначала" : IsWaiting ? "Отменить старт" : IsPlaying ? "Пауза" : "Пуск";

    /// <summary>
    /// Gets or sets the countdown text of a scheduled start.
    /// </summary>
    [ObservableProperty]
    public partial string CountdownText { get; set; } = string.Empty;

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
    /// Gets the hint shown instead of the video.
    /// </summary>
    public string VideoPlaceholder
        => HasMedia ? "Видео на этом компьютере недоступно, синхронизация продолжает работать"
            : IsHost ? "Выберите файл, который увидят все участники"
            : "Ведущий ещё не выбрал файл";

    /// <summary>
    /// Gets a value indicating whether the host can pick the first file from the placeholder.
    /// </summary>
    public bool CanPickFirstFile => IsHost && !HasMedia && !IsBusy;

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
        var name = await _settings.GetAsync(DisplayNameSetting, CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(name))
        {
            DisplayName = name;
        }

        OpenRouterPort = await _settings.GetAsync(OpenRouterPortSetting, CancellationToken.None) != "false";
        UpdateUrl = await _settings.GetAsync(UpdateService.UrlSetting, CancellationToken.None) ?? string.Empty;
        if (_updates is not null)
        {
            _updates.ManifestUrl = UpdateUrl;
            _ = CheckUpdatesAsync();
        }

        SideTab = await _settings.GetAsync(SideTabSetting, CancellationToken.None) == "events" ? SideTab.Events : SideTab.Chat;
        EventsExpanded = await _settings.GetAsync(EventsExpandedSetting, CancellationToken.None) != "false";
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
    }

    /// <summary>
    /// Starts hosting and shares a file, for example when the application was opened with a video file.
    /// </summary>
    /// <param name="path">The video file.</param>
    /// <returns>A task that completes when the file is shared.</returns>
    public Task HostFileAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return RunAsync("Подготовка файла…", async token =>
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Файл «{path}» не найден.", path);
            }

            if (!_session.IsActive)
            {
                await SaveNameAsync();
                await _session.StartHostingAsync(Path.GetFileNameWithoutExtension(path), DisplayName, (int)(Port ?? TlsTransportOptions.DefaultPort), token);
            }

            await _session.ShareMediaAsync(path, token);
        });
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
            case nameof(PlayerAvailable) or nameof(HasMedia) or nameof(IsAwaitingApproval) or nameof(IsBusy) or nameof(IsHost) or nameof(IsInSession):
                base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(ShowVideo)));
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
            Participants.Clear();
            IsHost = false;
            IsAwaitingApproval = false;
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
        VerificationText = snapshot.VerificationCode?.ToString() ?? string.Empty;
        HasMedia = snapshot.Media is not null;
        MediaCaption = snapshot.Media is { } media
            ? $"{media.FileName} · {DisplayFormat.Size(media.Length)}{(snapshot.UsesLocalCopy ? " · локальная копия" : string.Empty)}"
            : snapshot.IsHost ? "Выберите файл для показа" : "Ведущий ещё не выбрал файл";

        var local = snapshot.Local;
        var duration = local?.Snapshot.Duration;
        DurationSeconds = Math.Max(1, duration?.TotalSeconds ?? 1);
        DurationText = DisplayFormat.Position(duration);
        var position = local?.Snapshot.Position ?? local?.Expected;
        var seeking = local?.Snapshot is { IsLoaded: true, IsBuffering: true };
        if (!IsScrubbing && !seeking)
        {
            // While the player seeks or waits for data its position is stale: the timeline would jump back.
            PositionSeconds = Math.Clamp(position?.TotalSeconds ?? 0, 0, DurationSeconds);
        }

        PositionText = DisplayFormat.Position(position);
        IsPlaying = snapshot.Playback?.State == PlayState.Playing;
        IsAdvancing = IsPlaying && local?.StartsIn is null && local?.Snapshot is { IsPaused: false, IsBuffering: false };
        IsEnded = snapshot.Playback is { State: PlayState.Paused, Cause: PlaybackCause.Ended };
        IsWaiting = snapshot.Playback is { State: PlayState.Paused, Cause: PlaybackCause.WaitingForParticipants };
        CountdownText = IsWaiting
            ? DescribeWaiting(snapshot, duration)
            : local?.StartsIn is { } startsIn ? $"Старт через {Math.Ceiling(startsIn.TotalSeconds):0}…" : string.Empty;
        UpdateResume(snapshot, position, duration);
        var buffered = ComputeBufferedRanges(snapshot, duration);
        if (!buffered.SequenceEqual(BufferedRanges))
        {
            BufferedRanges = buffered;
        }
        Chat.Tick();
        FirewallBlocked = _session.FirewallBlocked;
        Drawing.CanDraw = snapshot.State == SessionState.Active;
        Drawing.Tick();
        VideoAspect = _playerControls?.GetVideoAspect() ?? 0;

        UpdateParticipants(snapshot, duration);
        UpdateSyncChip(snapshot);
        SecurityText = $"Ключи закреплены · {Participants.Count} из {SessionOptions.MaxParticipantsLimit}";
        var own = snapshot.Participants.FirstOrDefault(p => p.IsLocal)?.Status;
        SharingText = own is { BytesFromPeers: > 0 } or { BytesToPeers: > 0 }
            ? $"Обмен с участниками: получено {DisplayFormat.Size(own.BytesFromPeers)}, отдано {DisplayFormat.Size(own.BytesToPeers)}"
            : string.Empty;
        ClockText = snapshot.IsHost
            ? "Часы сеанса: этот компьютер"
            : snapshot.ClockUncertainty is { } uncertainty
                ? $"Часы: ±{(int)uncertainty.TotalMilliseconds} мс · пинг {DisplayFormat.Ping((int?)snapshot.RoundTrip?.TotalMilliseconds)}"
                : "Часы: синхронизация…";
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
    public string ResumeText => ResumeOffer is { } offer ? $"В прошлый раз вы остановились на {DisplayFormat.Position(offer)}" : string.Empty;

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
            _resume.Report(id, current, duration, IsPlaying, DateTimeOffset.UtcNow);
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
    /// Gets or sets the address of the update source; an empty address switches the check off.
    /// </summary>
    [ObservableProperty]
    public partial string UpdateUrl { get; set; } = string.Empty;

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
    /// Gets or sets the progress of the download, 0–100.
    /// </summary>
    [ObservableProperty]
    public partial double UpdateProgress { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the update is being downloaded.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDownloadingUpdate { get; set; }

    /// <summary>
    /// Gets a value indicating whether a newer version is offered.
    /// </summary>
    public bool HasUpdate => UpdateText.Length > 0;

    /// <summary>
    /// Stores the address of the update source and looks there at once.
    /// </summary>
    /// <param name="value">The new address.</param>
    partial void OnUpdateUrlChanged(string value)
    {
        if (_updates is null || value == _updates.ManifestUrl)
        {
            return;
        }

        _updates.ManifestUrl = value.Trim();
        _ = _settings.SetAsync(UpdateService.UrlSetting, _updates.ManifestUrl, CancellationToken.None);
        _ = CheckUpdatesAsync();
    }

    /// <summary>
    /// Asks the update source whether a newer version exists.
    /// </summary>
    /// <returns>A task that completes when the answer is shown.</returns>
    public async Task CheckUpdatesAsync()
    {
        if (_updates is not { } updates)
        {
            return;
        }

        var found = await updates.CheckAsync(CancellationToken.None);
        _update = found;
        UpdateText = found is null
            ? string.Empty
            : $"Есть версия {found.Version}{(UpdateService.DescribeSize(found.Size) is { Length: > 0 } size ? " · " + size : string.Empty)}";
        UpdateNotes = found?.Notes ?? string.Empty;
    }

    /// <summary>
    /// Downloads the offered version, checks its hash and shows where it is.
    /// </summary>
    /// <returns>A task that completes when the file is downloaded.</returns>
    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        if (_updates is not { } updates || _update is not { } update || IsDownloadingUpdate)
        {
            return;
        }

        IsDownloadingUpdate = true;
        UpdateProgress = 0;
        try
        {
            var progress = new Progress<double>(value => _dispatcher.Post(() => UpdateProgress = value * 100));
            var path = await updates.DownloadAsync(update, _updateFolder, progress, CancellationToken.None);
            UpdateText = string.Empty;
            await _dialogs.ShowMessageAsync(
                "Обновление загружено",
                $"Файл проверен по контрольной сумме и лежит здесь:{Environment.NewLine}{path}{Environment.NewLine}{Environment.NewLine}" +
                "Закройте Golether и запустите его, чтобы обновиться. Ваши данные в папке data не тронутся.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync("Обновление не загружено", ex.Message);
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    /// <summary>
    /// Hides the offer until the next check.
    /// </summary>
    [RelayCommand]
    private void DismissUpdate() => UpdateText = string.Empty;

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
                $"Плеер: {(PlayerNotice.Length > 0 ? PlayerNotice : "работает")}",
                $"Камеры и голос: {(ConferenceAvailable ? "работают" : ConferenceNotice)}",
                $"Микрофон {(MicrophoneMuted ? "выключен" : "включён")}, камера {(CameraOff ? "выключена" : "включена")}",
                $"Синхронизация: {SyncText}",
            };
            if (ReconnectText.Length > 0)
            {
                notes.Add("Переподключение: " + ReconnectText);
            }

            var path = await diagnostics.SaveAsync(_session.GetSnapshot(), notes);
            await _dialogs.ShowMessageAsync(
                "Отчёт готов",
                $"Отчёт сохранён в файл:{Environment.NewLine}{path}{Environment.NewLine}{Environment.NewLine}" +
                "В нём нет ключей и паролей. Его можно отправить тому, кто помогает разобраться.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync("Отчёт не сохранён", ex.Message);
        }
    }

    /// <summary>
    /// Starts hosting a session.
    /// </summary>
    /// <returns>A task that completes when the session runs.</returns>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartHostingAsync() => RunAsync("Создание сеанса…", async token =>
    {
        await SaveNameAsync();
        await _session.StartHostingAsync(NewSessionName, DisplayName, (int)(Port ?? TlsTransportOptions.DefaultPort), token);
    });

    /// <summary>
    /// Joins a session.
    /// </summary>
    /// <returns>A task that completes when the host admitted this device.</returns>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task JoinAsync() => RunAsync("Подключение к ведущему…", async token =>
    {
        await SaveNameAsync();
        await _session.JoinAsync(InviteLink, DisplayName, token);
        InviteLink = string.Empty;
    });

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

        await RunAsync("Подготовка файла…", async token =>
        {
            if (_session.IsHost)
            {
                await _session.ShareMediaAsync(path, token);
            }
            else if (!await _session.UseLocalCopyAsync(path, token))
            {
                await _dialogs.ShowErrorAsync("Другой файл", "Выбранный файл отличается от того, что показывает ведущий.");
            }
        });
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
    /// <returns>The text, for example <c>Ждём: Марина и Олег…</c>.</returns>
    internal static string DescribeWaiting(SessionSnapshot snapshot, TimeSpan? duration)
    {
        var target = snapshot.Playback?.Position ?? TimeSpan.Zero;
        var names = snapshot.Participants
            .Where(p => !p.Info.IsHost && (p.Status is not { } status
                || !ReadinessOptions.IsReady(status, duration)
                || status.Position is not { } position
                || (position - target).Duration() > ReadinessOptions.WaitThreshold))
            .Select(p => p.Info.DisplayName)
            .ToArray();
        return names.Length switch
        {
            0 => "Ждём готовности участников…",
            1 => $"Ждём: {names[0]}…",
            2 => $"Ждём: {names[0]} и {names[1]}…",
            _ => $"Ждём: {names[0]}, {names[1]} и ещё {names.Length - 2}…",
        };
    }

    /// <summary>
    /// Seeks back by ten seconds.
    /// </summary>
    /// <returns>A task that completes when the intent was sent.</returns>
    [RelayCommand]
    private Task SeekBackAsync() => SeekToAsync(Math.Max(0, PositionSeconds - SeekStep.TotalSeconds));

    /// <summary>
    /// Seeks forward by ten seconds.
    /// </summary>
    /// <returns>A task that completes when the intent was sent.</returns>
    [RelayCommand]
    private Task SeekForwardAsync() => SeekToAsync(Math.Min(DurationSeconds, PositionSeconds + SeekStep.TotalSeconds));

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
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            await _dialogs.ShowErrorAsync("Команда не отправлена", ex.Message);
        }
    }

    /// <summary>
    /// Runs an operation with the busy indicator and shows errors.
    /// </summary>
    /// <param name="text">The progress text.</param>
    /// <param name="operation">The operation.</param>
    /// <returns>A task that completes when the operation finished.</returns>
    private async Task RunAsync(string text, Func<CancellationToken, Task> operation)
    {
        IsBusy = true;
        BusyText = text;
        try
        {
            await operation(CancellationToken.None);
        }
        catch (Exception ex) when (ex is FormatException or SessionJoinException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync("Не получилось", ex.Message);
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
        await _settings.SetAsync(DisplayNameSetting, DisplayName, CancellationToken.None);
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
        while (Events.Count > MaxEvents)
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
                item = new ParticipantItemViewModel(views[i].Info.PeerId, SwitchOffParticipantDevicesAsync, ChangeVoiceVolume);
                Participants.Insert(Math.Min(i, Participants.Count), item);
                if (!views[i].IsLocal)
                {
                    _ = RestoreVoiceVolumeAsync(item);
                }
            }

            item.Update(views[i], duration);
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
            await _settings.SetAsync(VoiceVolumeSettingPrefix + peer.Value, percent.ToString(CultureInfo.InvariantCulture), CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Applies the stored voice volume of a participant.
    /// </summary>
    /// <param name="item">The participant tile.</param>
    /// <returns>A task that completes when the volume is applied.</returns>
    private async Task RestoreVoiceVolumeAsync(ParticipantItemViewModel item)
    {
        var stored = await _settings.GetAsync(VoiceVolumeSettingPrefix + item.PeerId.Value, CancellationToken.None);
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
            await _dialogs.ShowErrorAsync("Не удалось выключить устройство", ex.Message);
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
            SyncText = "Ожидание ведущего";
            SyncLevel = IndicatorLevel.Warning;
            return;
        }

        var drifts = snapshot.Participants.Select(p => p.Status).OfType<Core.Session.ParticipantStatus>().Where(s => s.Position is not null).ToArray();
        if (drifts.Length == 0 || snapshot.Playback?.State != PlayState.Playing)
        {
            SyncText = snapshot.Playback?.State == PlayState.Playing ? "Синхронизация…" : IsEnded ? "Фильм закончился" : "На паузе";
            SyncLevel = IndicatorLevel.Neutral;
            return;
        }

        var worst = drifts.Max(s => s.Drift.Duration());
        SyncText = $"Синхронно · Δ ≤ {(int)worst.TotalMilliseconds} мс";
        SyncLevel = DisplayFormat.DriftLevel(worst);
        if (drifts.Any(s => s.IsBuffering))
        {
            SyncText = "Кто-то буферизует";
            SyncLevel = IndicatorLevel.Warning;
        }
    }
}
