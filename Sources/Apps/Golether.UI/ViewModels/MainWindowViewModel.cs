using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Components.Catalog;
using Golether.Core.Data.Stores;
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
    public MainWindowViewModel(
        ISessionService session,
        IDialogService dialogs,
        ISettingsStore settings,
        IUiDispatcher dispatcher,
        Func<string, TunnelDialogViewModel> tunnelDialogFactory,
        string deviceFingerprint,
        IConferenceMedia conference,
        IComponentService components,
        ILocalPlayerControls? playerControls = null)
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
        Volume = playerControls is null ? null : new VolumeViewModel(playerControls, _settings);
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
    /// Gets or sets a value indicating whether the event feed is shown.
    /// </summary>
    [ObservableProperty]
    public partial bool EventsExpanded { get; set; } = true;

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
    /// Shows or hides the event feed and remembers the choice.
    /// </summary>
    [RelayCommand]
    private void ToggleEvents()
    {
        EventsExpanded = !EventsExpanded;
        _ = _settings.SetAsync(EventsExpandedSetting, EventsExpanded ? "true" : "false", CancellationToken.None);
    }

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
    public partial bool IsPlaying { get; set; }

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
        EventsExpanded = await _settings.GetAsync(EventsExpandedSetting, CancellationToken.None) != "false";
        if (Volume is not null)
        {
            await Volume.LoadAsync();
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
        IsInSession = snapshot is not null && snapshot.State != SessionState.Ended;
        if (snapshot is null)
        {
            Participants.Clear();
            IsHost = false;
            IsAwaitingApproval = false;
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
        if (!IsScrubbing)
        {
            PositionSeconds = Math.Clamp(position?.TotalSeconds ?? 0, 0, DurationSeconds);
        }

        PositionText = DisplayFormat.Position(position);
        IsPlaying = snapshot.Playback?.State == PlayState.Playing;
        CountdownText = local?.StartsIn is { } startsIn ? $"Старт через {Math.Ceiling(startsIn.TotalSeconds):0}…" : string.Empty;

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
        => SendAsync(new PlaybackRequest(IsPlaying ? PlaybackRequestKind.Pause : PlaybackRequestKind.Play, IsPlaying ? TimeSpan.FromSeconds(PositionSeconds) : null));

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
                item = new ParticipantItemViewModel(views[i].Info.PeerId, SwitchOffParticipantDevicesAsync);
                Participants.Insert(Math.Min(i, Participants.Count), item);
            }

            item.Update(views[i], duration);
            item.CanModerate = IsHost && !item.IsLocal;
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
            SyncText = snapshot.Playback?.State == PlayState.Playing ? "Синхронизация…" : "На паузе";
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
