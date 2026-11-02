using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Client.Localization;
using Golether.Client.Services;
using Golether.Client.ViewModels;
using Golether.Core.Data.Enums;
using Golether.Core.Identity;
using Golether.Core.Session;
using Golether.Localization;
using Golether.Media.Conference;
using Golether.Media.Player.Mpv;
using Golether.Mobile.Enums;
using Golether.Mobile.Services;
using Golether.Security.Invites;
using Golether.Security.Verification;
using Golether.Session;
using Golether.Sync.Protocol;

namespace Golether.Mobile.ViewModels;

/// <summary>
/// The whole of the mobile client as a participant: joining by an invitation, waiting for the host, watching.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, ICameraTiles
{
    /// <summary>
    /// The assembly that holds the flags of the languages.
    /// </summary>
    private const string FLAGS_ASSEMBLY = "Golether.Mobile";

    /// <summary>
    /// The size of the subtitles on an upright screen, where the picture is low and they follow its height.
    /// </summary>
    private const double UPRIGHT_SUBTITLE_SCALE = 1.5;

    /// <summary>
    /// How far the skip buttons move the film.
    /// </summary>
    private static readonly TimeSpan _skip = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The UI dispatcher.
    /// </summary>
    private readonly IUiDispatcher _dispatcher;

    /// <summary>
    /// The system.
    /// </summary>
    private readonly IMobilePlatform _platform;

    /// <summary>
    /// Connects the chat, the pen and the shown pictures to the session of the moment.
    /// </summary>
    private readonly CurrentSession _current = new();

    /// <summary>
    /// Gives the file picker of the system.
    /// </summary>
    private readonly Func<IStorageProvider?> _storage;

    /// <summary>
    /// Prepares the services.
    /// </summary>
    private readonly Func<IFeatureAccess, Task<IMobileServices>> _start;

    /// <summary>
    /// The paid features. Nothing can be bought yet, so everything is open until the store comes.
    /// </summary>
    private readonly IFeatureAccess _features = new AllFeaturesAccess();

    /// <summary>
    /// The pictures the participants chose.
    /// </summary>
    private readonly PeerAvatars _avatars;

    /// <summary>
    /// The services, once they are ready.
    /// </summary>
    private IMobileServices? _services;

    /// <summary>
    /// The cameras and voices, once the first session started them.
    /// </summary>
    private IConferenceMedia? _conference;

    /// <summary>
    /// The place of this device in a session, or <see langword="null"/> outside one.
    /// </summary>
    private IParticipantConnection? _connection;

    /// <summary>
    /// Cancels joining.
    /// </summary>
    private CancellationTokenSource? _joining;

    /// <summary>
    /// Whether a call took the microphone, so it comes back with the end of the call.
    /// </summary>
    private bool _microphoneTaken;

    /// <summary>
    /// The verification code on the screen.
    /// </summary>
    private string _shownVerification = string.Empty;

    /// <summary>
    /// Whether the screen stands upright.
    /// </summary>
    private bool _uprightScreen = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class.
    /// </summary>
    /// <param name="platform">The system.</param>
    /// <param name="dispatcher">The UI dispatcher.</param>
    /// <param name="storage">Gives the file picker of the system.</param>
    /// <param name="start">Prepares the services.</param>
    /// <param name="decodeAvatar">Turns the bytes of a chosen picture into an image, or <see langword="null"/> for a bitmap.</param>
    public MainViewModel(
        IMobilePlatform platform,
        IUiDispatcher dispatcher,
        Func<IStorageProvider?> storage,
        Func<IFeatureAccess, Task<IMobileServices>> start,
        Func<byte[], object?>? decodeAvatar = null)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _start = start ?? throw new ArgumentNullException(nameof(start));
        _avatars = new PeerAvatars(decodeAvatar);

        _platform.Returned += (_, _) => RestartCamera();
        _platform.AudioInterrupted += (_, interrupted) => _dispatcher.Post(() => OnAudioInterrupted(interrupted));
        _platform.NetworkReturned += (_, _) => _connection?.RetryNow();

        Chat = new ChatViewModel(_current, dispatcher);
        Drawing = new DrawingViewModel(_current, dispatcher, _features);
        Gallery = new GalleryViewModel(_current, new SkiaPictureCodec());
        Gallery.PictureChanged += (_, _) => Drawing.Reset();
    }

    /// <summary>
    /// Raised when the player is ready, so the video view can show it.
    /// </summary>
    public event EventHandler? PlayerReady;

    /// <summary>
    /// Raised once when the cameras and voices are ready, so the view can show the frames.
    /// </summary>
    public event EventHandler<IConferenceMedia>? ConferenceReady;

    /// <summary>
    /// Gets the player, once it is ready.
    /// </summary>
    public MpvPlayer? Player => _services?.Player;

    /// <summary>
    /// Gets or sets the current screen.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsJoin), nameof(IsConnecting), nameof(IsApproval), nameof(IsWatch))]
    public partial MobileScreen Screen { get; set; }

    /// <summary>
    /// Gets a value indicating whether the join screen is shown.
    /// </summary>
    public bool IsJoin => Screen == MobileScreen.Join;

    /// <summary>
    /// Gets a value indicating whether the search for the host is shown.
    /// </summary>
    public bool IsConnecting => Screen == MobileScreen.Connecting;

    /// <summary>
    /// Gets a value indicating whether the code for the host is shown.
    /// </summary>
    public bool IsApproval => Screen == MobileScreen.Approval;

    /// <summary>
    /// Gets a value indicating whether the session is shown.
    /// </summary>
    public bool IsWatch => Screen == MobileScreen.Watch;

    /// <summary>
    /// Gets or sets the name of the user.
    /// </summary>
    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the key or the link of the invitation.
    /// </summary>
    [ObservableProperty]
    public partial string InviteLink { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the reason of the last failure, empty when there is none.
    /// </summary>
    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how the way back to the session goes, empty when the connection is fine.
    /// </summary>
    [ObservableProperty]
    public partial string ReconnectMessage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the words and the number of the verification code to read to the host.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> VerificationParts { get; set; } = [];

    /// <summary>
    /// Gets or sets the name of the session.
    /// </summary>
    [ObservableProperty]
    public partial string SessionName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the position as text.
    /// </summary>
    [ObservableProperty]
    public partial string PositionText { get; set; } = DisplayFormat.Position(null);

    /// <summary>
    /// Gets or sets the length of the film as text.
    /// </summary>
    [ObservableProperty]
    public partial string DurationText { get; set; } = DisplayFormat.Position(null);

    /// <summary>
    /// Gets or sets the position in seconds, for the timeline.
    /// </summary>
    [ObservableProperty]
    public partial double PositionSeconds { get; set; }

    /// <summary>
    /// Gets or sets the length of the film in seconds, for the timeline.
    /// </summary>
    [ObservableProperty]
    public partial double DurationSeconds { get; set; } = 1;

    /// <summary>
    /// Gets or sets a value indicating whether the film is paused.
    /// </summary>
    [ObservableProperty]
    public partial bool IsPaused { get; set; } = true;

    /// <summary>
    /// Gets the chat and the reactions.
    /// </summary>
    public ChatViewModel Chat { get; }

    /// <summary>
    /// Gets the pen.
    /// </summary>
    public DrawingViewModel Drawing { get; }

    /// <summary>
    /// Gets the shown pictures.
    /// </summary>
    public GalleryViewModel Gallery { get; }

    /// <summary>
    /// Gets or sets the sound and subtitle tracks of this device, once the player is ready.
    /// </summary>
    [ObservableProperty]
    public partial TracksViewModel? Tracks { get; set; }

    /// <summary>
    /// Gets or sets the volume of the film on this device, once the player is ready.
    /// </summary>
    [ObservableProperty]
    public partial VolumeViewModel? Volume { get; set; }

    /// <summary>
    /// Gets or sets the languages of the application.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<LanguageOption> Languages { get; set; } = [];

    /// <summary>
    /// Gets or sets the language of the application.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LanguageCode))]
    public partial LanguageOption? Language { get; set; }

    /// <summary>
    /// Gets the code of the language, as the button of the languages shows it.
    /// </summary>
    public string LanguageCode => Language?.Code.ToUpperInvariant() ?? string.Empty;

    /// <summary>
    /// Gets a value indicating whether the camera can be switched to the other side of the device.
    /// </summary>
    public bool CanSwitchCamera => _platform.CanSwitchCamera;

    /// <summary>
    /// Gets or sets the width-to-height ratio of the film, which the strokes follow.
    /// </summary>
    [ObservableProperty]
    public partial double VideoAspect { get; set; }

    /// <inheritdoc />
    public ObservableCollection<ParticipantItemViewModel> Participants { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the microphone of this device is off.
    /// </summary>
    [ObservableProperty]
    public partial bool MicrophoneMuted { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the camera of this device is off.
    /// </summary>
    [ObservableProperty]
    public partial bool CameraOff { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user holds the timeline, so the film does not move it.
    /// </summary>
    public bool IsDragging { get; set; }

    /// <summary>
    /// Prepares the application and shows the join screen.
    /// </summary>
    /// <returns>A task that completes when the application is ready.</returns>
    public async Task StartAsync()
    {
        try
        {
            var services = await Task.Run(() => _start(_features));
            _services = services;
            OnPropertyChanged(nameof(Player));
            PlayerReady?.Invoke(this, EventArgs.Empty);
            FitSubtitles(_uprightScreen);

            services.Controls.Failed += (_, text) => _dispatcher.Post(() => Error = text);
            var dialogs = new MobileDialogs(_storage, services.TrackFiles, text => Error = text);
            Tracks = new TracksViewModel(services.Controls, services.Settings, dialogs, _dispatcher, _features);
            await Tracks.LoadAsync();
            Volume = new VolumeViewModel(services.Controls, services.Settings);
            await Volume.LoadAsync();

            Languages = [.. services.Language.Languages.Select(l => new LanguageOption(l, FlagImages.Load(FLAGS_ASSEMBLY, l.Code)))];
            Language = Languages.FirstOrDefault(l => l.Language == services.Language.Current);

            var name = await services.Settings.GetAsync(ClientSettings.DISPLAY_NAME, CancellationToken.None);
            DisplayName = name ?? string.Empty;
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            Error = ex.Message;
        }

        Screen = MobileScreen.Join;
    }

    /// <summary>
    /// Puts an invitation that came from a link into the join screen.
    /// </summary>
    /// <param name="link">The key or the link of the invitation.</param>
    public void OpenInvite(string link)
    {
        if (_connection is null)
        {
            InviteLink = link;
        }
    }

    /// <summary>
    /// Reads the state of the session and of the player into the screen.
    /// </summary>
    public void Refresh()
    {
        if (_connection is not { } connection || _services is not { } services || connection.GetSnapshot() is not { } snapshot)
        {
            return;
        }

        SessionName = snapshot.SessionName;
        ShowVerification(snapshot.VerificationCode);
        ReconnectMessage = connection.ReconnectMessage;

        if (snapshot.State == SessionState.Ended && !connection.IsReconnecting)
        {
            _ = FinishAsync(connection.EndReason);
            return;
        }

        // On the way back the film and the people stay on the screen, with the state of the connection over them.
        Screen = connection.IsReconnecting
            ? MobileScreen.Watch
            : snapshot.State switch
            {
                SessionState.AwaitingApproval => MobileScreen.Approval,
                SessionState.Active => MobileScreen.Watch,
                _ => MobileScreen.Connecting,
            };

        var player = services.Playback.GetSnapshot();
        UpdateParticipants(snapshot, player.Duration);
        Gallery.Update(snapshot, Participants);
        Drawing.CanDraw = snapshot.State == SessionState.Active && (!Gallery.IsActive || Gallery.CanDraw);
        Drawing.Tick();
        Chat.Tick();
        VideoAspect = Gallery.ShowsPicture ? Gallery.PictureAspect : services.Controls.GetVideoAspect();
        IsPaused = player.IsPaused;
        PositionText = DisplayFormat.Position(player.Position);
        DurationText = DisplayFormat.Position(player.Duration);
        DurationSeconds = Math.Max(player.Duration?.TotalSeconds ?? 0, 1);
        if (!IsDragging)
        {
            PositionSeconds = player.Position?.TotalSeconds ?? 0;
        }
    }

    /// <summary>
    /// Answers the back button of the system: inside a session it never ends the session, which only the button for
    /// that does. What lies on top goes first, then the application leaves the screen.
    /// </summary>
    /// <returns><see langword="true"/> when the button was used here.</returns>
    public bool GoBack()
    {
        if (_connection is null)
        {
            return false;
        }

        if (Gallery.IsGridOpen)
        {
            Gallery.CloseGridCommand.Execute(null);
        }
        else if (Gallery.IsAlone)
        {
            Gallery.ReturnToCommonCommand.Execute(null);
        }
        else if (Drawing.IsPenActive)
        {
            Drawing.TogglePen();
        }
        else
        {
            _platform.MoveToBackground();
        }

        return true;
    }

    /// <summary>
    /// Restarts the camera: after a turn of the screen its picture must stand upright again, and away from the screen
    /// the system takes it from the application.
    /// </summary>
    public void RestartCamera()
    {
        if (_connection is not null && _conference is { IsAvailable: true } conference && !CameraOff)
        {
            _ = conference.StartCaptureAsync(null, null, CancellationToken.None);
        }
    }

    /// <summary>
    /// Fits the subtitles to the position of the screen.
    /// </summary>
    /// <param name="upright">Whether the screen stands upright.</param>
    public void FitSubtitles(bool upright)
    {
        _uprightScreen = upright;
        _services?.Controls.SetSubtitleScale(upright ? UPRIGHT_SUBTITLE_SCALE : 1);
    }

    /// <summary>
    /// Moves the film for everybody to where the user let the timeline go.
    /// </summary>
    /// <param name="seconds">The position in seconds.</param>
    public void SeekTo(double seconds) => Request(new PlaybackRequest(PlaybackRequestKind.Seek, TimeSpan.FromSeconds(seconds)));

    /// <summary>
    /// Connects to the host of the invitation.
    /// </summary>
    /// <returns>A task that completes when the host has answered.</returns>
    [RelayCommand]
    private async Task JoinAsync()
    {
        if (_services is not { } services)
        {
            return;
        }

        Error = string.Empty;
        Screen = MobileScreen.Connecting;
        var cancellation = new CancellationTokenSource();
        _joining = cancellation;

        try
        {
            var invite = Invite.ParseLink(InviteLink.Trim());
            var name = ParticipantInfo.NormalizeDisplayName(DisplayName);
            DisplayName = name;
            await services.Settings.SetAsync(ClientSettings.DISPLAY_NAME, name, CancellationToken.None);

            var access = await _platform.RequestCaptureAccessAsync(camera: true, microphone: true);
            var conference = await UseConferenceAsync(services);
            conference.SetMuted(MicrophoneMuted || !access.Microphone, CameraOff || !access.Camera);
            ReadDevices();

            var connection = services.CreateConnection(conference, _current, name);
            connection.Changed += (_, _) => _dispatcher.Post(Refresh);
            connection.AvatarReceived += (_, update) => _dispatcher.Post(() => ShowAvatar(update));
            _connection = connection;
            _platform.SetSessionActive(true);

            await connection.JoinAsync(invite, cancellation.Token);
            Refresh();
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            await FinishAsync(ex is OperationCanceledException ? null : ex.Message);
        }
    }

    /// <summary>
    /// Takes a language the user chose.
    /// </summary>
    /// <param name="language">The language.</param>
    [RelayCommand]
    private void ChooseLanguage(LanguageOption language) => Language = language;

    /// <summary>
    /// Takes away a message the user has read.
    /// </summary>
    [RelayCommand]
    private void ClearError() => Error = string.Empty;

    /// <summary>
    /// Takes the camera on the other side of the device.
    /// </summary>
    [RelayCommand]
    private void SwitchCamera()
    {
        _platform.SwitchCamera();
        RestartCamera();
    }

    /// <summary>
    /// Stops joining or leaves the session.
    /// </summary>
    /// <returns>A task that completes when the session is closed.</returns>
    [RelayCommand]
    private async Task LeaveAsync()
    {
        _joining?.Cancel();
        await FinishAsync(null);
    }

    /// <summary>
    /// Switches the microphone off or on; to switch it on the system must allow it.
    /// </summary>
    /// <returns>A task that completes when the user has answered the system.</returns>
    [RelayCommand]
    private async Task ToggleMicrophoneAsync()
    {
        if (_conference is not { } conference)
        {
            return;
        }

        Error = string.Empty;
        if (MicrophoneMuted && !(await _platform.RequestCaptureAccessAsync(camera: false, microphone: true)).Microphone)
        {
            Error = Texts.Get("Mobile.Access.Microphone");
            return;
        }

        conference.SetMuted(!MicrophoneMuted, CameraOff);
        ReadDevices();

        // The service keeps the microphone off the screen only if it knew about the permission when it started.
        _platform.SetSessionActive(true);
    }

    /// <summary>
    /// Switches the camera off or on; to switch it on the system must allow it.
    /// </summary>
    /// <returns>A task that completes when the user has answered the system.</returns>
    [RelayCommand]
    private async Task ToggleCameraAsync()
    {
        if (_conference is not { } conference)
        {
            return;
        }

        Error = string.Empty;
        if (CameraOff && !(await _platform.RequestCaptureAccessAsync(camera: true, microphone: false)).Camera)
        {
            Error = Texts.Get("Mobile.Access.Camera");
            return;
        }

        conference.SetMuted(MicrophoneMuted, !CameraOff);
        ReadDevices();
    }

    /// <summary>
    /// Starts or pauses the film for everybody.
    /// </summary>
    [RelayCommand]
    private void TogglePlay() => Request(new PlaybackRequest(IsPaused ? PlaybackRequestKind.Play : PlaybackRequestKind.Pause, null));

    /// <summary>
    /// Moves the film back for everybody.
    /// </summary>
    [RelayCommand]
    private void SkipBack() => SeekTo(Math.Max(PositionSeconds - _skip.TotalSeconds, 0));

    /// <summary>
    /// Moves the film forward for everybody.
    /// </summary>
    [RelayCommand]
    private void SkipForward() => SeekTo(Math.Min(PositionSeconds + _skip.TotalSeconds, DurationSeconds));

    /// <summary>
    /// Applies a language the user chose.
    /// </summary>
    /// <param name="value">The language.</param>
    partial void OnLanguageChanged(LanguageOption? value)
    {
        if (value is not null && _services is { } services && value.Language != services.Language.Current)
        {
            _ = ChangeLanguageAsync(services, value.Language);
        }
    }

    /// <summary>
    /// Takes a language into use; the language stays changed even if it could not be remembered.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="language">The language.</param>
    /// <returns>A task that completes when the language is taken.</returns>
    private async Task ChangeLanguageAsync(IMobileServices services, LanguageInfo language)
    {
        try
        {
            await services.Language.SelectAsync(language.Code, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Error = Texts.Get("Language.Error.Title");
        }

        Volume?.RefreshTexts();
    }

    /// <summary>
    /// Keeps a call private: the film goes silent and the microphone is off until it ends.
    /// </summary>
    /// <param name="interrupted">Whether the sound was taken away.</param>
    private void OnAudioInterrupted(bool interrupted)
    {
        if (_connection is null || _services is not { } services)
        {
            return;
        }

        services.Controls.SetMuted(interrupted || Volume is { IsMuted: true });

        if (_conference is not { } conference)
        {
            return;
        }

        if (interrupted && !MicrophoneMuted)
        {
            _microphoneTaken = true;
            conference.SetMuted(true, CameraOff);
            ReadDevices();
        }
        else if (!interrupted && _microphoneTaken)
        {
            _microphoneTaken = false;
            conference.SetMuted(false, CameraOff);
            ReadDevices();
        }
    }

    /// <summary>
    /// Takes the cameras and voices into use; the first time it follows their events.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <returns>The conference.</returns>
    private async Task<IConferenceMedia> UseConferenceAsync(IMobileServices services)
    {
        if (_conference is { } ready)
        {
            return ready;
        }

        var conference = await services.GetConferenceAsync();
        conference.SpeakingChanged += (_, change) => _dispatcher.Post(() => ShowSpeaking(change));
        conference.MuteChanged += (_, _) => _dispatcher.Post(ReadDevices);
        conference.MicrophoneFailed += (_, _) => _dispatcher.Post(ReadDevices);
        conference.CameraFailed += (_, _) => _dispatcher.Post(OnCameraFailed);
        _conference = conference;
        ConferenceReady?.Invoke(this, conference);

        return conference;
    }

    /// <summary>
    /// Switches off a camera that stopped, and says so. Off the screen that is no failure: the system took the camera
    /// away, and it comes back with the application.
    /// </summary>
    private void OnCameraFailed()
    {
        if (_connection is null || _conference is not { } conference || !_platform.IsOnScreen || CameraOff)
        {
            return;
        }

        Error = Texts.Get("Mobile.Camera.Failed");
        conference.SetMuted(MicrophoneMuted, true);
        ReadDevices();
    }

    /// <summary>
    /// Puts the verification code on the screen; it stays the same for the whole wait.
    /// </summary>
    /// <param name="code">The code, or <see langword="null"/>.</param>
    private void ShowVerification(VerificationCode? code)
    {
        var text = code?.ToString() ?? string.Empty;
        if (text == _shownVerification)
        {
            return;
        }

        _shownVerification = text;
        VerificationParts = code is null ? [] : [.. code.Words, code.Number.ToString("00", CultureInfo.InvariantCulture)];
    }

    /// <summary>
    /// Shows the picture a participant chose.
    /// </summary>
    /// <param name="update">The picture.</param>
    private void ShowAvatar(AvatarUpdate update)
    {
        var image = _avatars.Apply(update);

        if (Participants.FirstOrDefault(p => p.PeerId == update.Peer) is { } tile)
        {
            tile.AvatarImage = image;
        }
    }

    /// <summary>
    /// Reads the state of the devices: the host or a failure can switch them off.
    /// </summary>
    private void ReadDevices()
    {
        if (_conference is not { } conference)
        {
            return;
        }

        MicrophoneMuted = conference.MicrophoneMuted;
        CameraOff = conference.CameraOff;

        if (CameraOff && Participants.FirstOrDefault(p => p.IsLocal) is { } local)
        {
            local.CameraImage = null;
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
    /// Synchronizes the tiles with the session.
    /// </summary>
    /// <param name="snapshot">The session.</param>
    /// <param name="duration">The length of the film.</param>
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

        foreach (var view in views)
        {
            var tile = Participants.FirstOrDefault(p => p.PeerId == view.Info.PeerId);
            if (tile is null)
            {
                tile = new ParticipantItemViewModel(view.Info.PeerId, voiceVolumeChanged: ChangeVoiceVolume)
                {
                    AvatarImage = _avatars.Find(view.Info.PeerId),
                };
                Participants.Add(tile);

                if (!view.IsLocal)
                {
                    _ = RestoreVoiceVolumeAsync(tile);
                }
            }

            tile.Update(view, duration);
        }
    }

    /// <summary>
    /// Applies the volume of a voice and remembers it for later sessions.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="percent">The volume in percent.</param>
    private void ChangeVoiceVolume(PeerId peer, double percent)
    {
        _conference?.SetVoiceVolume(peer, percent / 100);

        if (_services is { } services)
        {
            _ = services.VoiceVolumes.SaveAsync(peer, percent);
        }
    }

    /// <summary>
    /// Applies the remembered volume of a voice.
    /// </summary>
    /// <param name="tile">The participant.</param>
    /// <returns>A task that completes when the volume is applied.</returns>
    private async Task RestoreVoiceVolumeAsync(ParticipantItemViewModel tile)
    {
        if (_services is { } services && await services.VoiceVolumes.ReadAsync(tile.PeerId) is { } percent && percent != tile.VoiceVolume)
        {
            tile.RestoreVoiceVolume(percent);
            _conference?.SetVoiceVolume(tile.PeerId, percent / 100);
        }
    }

    /// <summary>
    /// Sends a request of the user to the host; a failure is shown, it does not end the session.
    /// </summary>
    /// <param name="request">The request.</param>
    private async void Request(PlaybackRequest request)
    {
        if (_connection is not { } connection)
        {
            return;
        }

        try
        {
            await connection.RequestAsync(request);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            Error = ex.Message;
        }
    }

    /// <summary>
    /// Closes the session and returns to the join screen.
    /// </summary>
    /// <param name="reason">The reason to show, or <see langword="null"/>.</param>
    /// <returns>A task that completes when the session is closed.</returns>
    private async Task FinishAsync(string? reason)
    {
        var connection = _connection;
        _connection = null;
        _microphoneTaken = false;
        Screen = MobileScreen.Join;
        Error = reason ?? string.Empty;
        ReconnectMessage = string.Empty;
        Gallery.Update(null, Participants);
        Participants.Clear();
        _avatars.Clear();
        Chat.Reset();
        Drawing.CanDraw = false;
        Drawing.Reset();

        if (connection is not null)
        {
            _platform.SetSessionActive(false);
            await connection.DisposeAsync();
            _services?.Controls.ForgetFiles();
            _services?.TrackFiles.Clear();
        }
    }
}
