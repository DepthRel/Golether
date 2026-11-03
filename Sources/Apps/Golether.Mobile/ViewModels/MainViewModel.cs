using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Client.Enums;
using Golether.Client.Localization;
using Golether.Client.Services;
using Golether.Client.ViewModels;
using Golether.Core.Data.Enums;
using Golether.Core.Enums;
using Golether.Core.Identity;
using Golether.Core.Session;
using Golether.Localization;
using Golether.Media.Conference;
using Golether.Media.Player.Mpv;
using Golether.Media.Streaming.Files;
using Golether.Mobile.Enums;
using Golether.Mobile.Services;
using Golether.Security.Invites;
using Golether.Security.Verification;
using Golether.Session;
using Golether.Sync.Protocol;

namespace Golether.Mobile.ViewModels;

/// <summary>
/// The whole of the mobile client: joining by an invitation, waiting for the host and watching, or hosting a small
/// session of its own: showing, inviting and letting people in.
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
    /// The largest image the picture of the user may be chosen from: a photo of a phone is far smaller, and a bigger
    /// file would only fill the memory.
    /// </summary>
    private const long MAX_AVATAR_SOURCE_BYTES = 32L * 1024 * 1024;

    /// <summary>
    /// How far the skip buttons move the film.
    /// </summary>
    private static readonly TimeSpan _skip = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The shortest time between two sounds of new messages: a burst of messages claps once, not in a rattle.
    /// </summary>
    private static readonly TimeSpan _messageSoundInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The version of the application, as three numbers; the package of the application carries the same.
    /// </summary>
    private static readonly string _version = (typeof(MainViewModel).Assembly.GetName().Version ?? new Version(0, 0, 0)).ToString(3);

    /// <summary>
    /// The UI dispatcher.
    /// </summary>
    private readonly IUiDispatcher _dispatcher;

    /// <summary>
    /// The time source.
    /// </summary>
    private readonly TimeProvider _timeProvider;

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
    /// Decodes the shown pictures and the image of the picture of the user, upright.
    /// </summary>
    private readonly SkiaPictureCodec _pictures = new();

    /// <summary>
    /// The picture of the user as the others get it, or <see langword="null"/> when there is none.
    /// </summary>
    private byte[]? _ownAvatar;

    /// <summary>
    /// The sound of a new message, the clapperboard of the desktop, read once when it is first needed.
    /// </summary>
    private readonly Lazy<byte[]?> _messageSound = new(LoadMessageSound);

    /// <summary>
    /// When the sound of a new message was last played (a timestamp of <see cref="_timeProvider"/>), or
    /// <see langword="null"/>.
    /// </summary>
    private long? _messageSoundPlayed;

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
    private ISessionConnection? _connection;

    /// <summary>
    /// Asks the user, as the host, whether a participant may come in.
    /// </summary>
    private readonly MobileAdmissionPrompt _admissions = new();

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
    /// The name of a new session as it was offered last: the offer follows the language until the user writes another.
    /// </summary>
    private string _offeredSessionName = Texts.Get("Main.DefaultSessionName");

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class.
    /// </summary>
    /// <param name="platform">The system.</param>
    /// <param name="dispatcher">The UI dispatcher.</param>
    /// <param name="storage">Gives the file picker of the system.</param>
    /// <param name="start">Prepares the services.</param>
    /// <param name="decodeAvatar">Turns the bytes of a chosen picture into an image, or <see langword="null"/> for a bitmap.</param>
    /// <param name="timeProvider">The time source, or <see langword="null"/> for the system clock.</param>
    public MainViewModel(
        IMobilePlatform platform,
        IUiDispatcher dispatcher,
        Func<IStorageProvider?> storage,
        Func<IFeatureAccess, Task<IMobileServices>> start,
        Func<byte[], object?>? decodeAvatar = null,
        TimeProvider? timeProvider = null)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _start = start ?? throw new ArgumentNullException(nameof(start));
        _avatars = new PeerAvatars(decodeAvatar);

        _platform.Returned += (_, _) => RestartCamera();
        _platform.AudioInterrupted += (_, interrupted) => _dispatcher.Post(() => OnAudioInterrupted(interrupted));
        _platform.NetworkReturned += (_, _) => _connection?.RetryNow();

        Chat = new ChatViewModel(_current, dispatcher);
        Chat.LineAdded += (_, _) => AnnounceMessage();
        Drawing = new DrawingViewModel(_current, dispatcher, _features);
        Gallery = new GalleryViewModel(_current, _pictures);
        Gallery.PictureChanged += (_, _) => Drawing.Reset();
        Media = new MediaChoiceViewModel(platform, storage);
        _admissions.Changed += (_, _) => _dispatcher.Post(ShowAdmission);
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
    [NotifyPropertyChangedFor(nameof(IsJoin), nameof(IsHostSetup), nameof(IsConnecting), nameof(IsApproval), nameof(IsWatch))]
    public partial MobileScreen Screen { get; set; }

    /// <summary>
    /// Gets a value indicating whether the join screen is shown.
    /// </summary>
    public bool IsJoin => Screen == MobileScreen.Join;

    /// <summary>
    /// Gets a value indicating whether the screen of a new hosted session is shown.
    /// </summary>
    public bool IsHostSetup => Screen == MobileScreen.Host;

    /// <summary>
    /// Gets or sets a value indicating whether this device hosts the session: then it shows, invites and moderates.
    /// </summary>
    [ObservableProperty]
    public partial bool IsHost { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user is warned about hosting a session on a phone.
    /// </summary>
    [ObservableProperty]
    public partial bool IsHostWarningShown { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the warning about hosting stays away from now on.
    /// </summary>
    [ObservableProperty]
    public partial bool HostWarningHidden { get; set; }

    /// <summary>
    /// Gets or sets the name of the session to host.
    /// </summary>
    [ObservableProperty]
    public partial string NewSessionName { get; set; } = Texts.Get("Main.DefaultSessionName");

    /// <summary>
    /// Gets what the host chose to show.
    /// </summary>
    public MediaChoiceViewModel Media { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the host chooses something else to show.
    /// </summary>
    [ObservableProperty]
    public partial bool IsMediaChangeOpen { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the host's session starts or the chosen pictures are prepared.
    /// </summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// Gets or sets the invitation the host gives, or <see langword="null"/> while it is not shown.
    /// </summary>
    [ObservableProperty]
    public partial InviteViewModel? Invitation { get; set; }

    /// <summary>
    /// Gets or sets the participant who waits to come in, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    public partial AdmissionViewModel? Admission { get; set; }

    /// <summary>
    /// Gets or sets the participant the host is about to remove, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemovalText))]
    public partial ParticipantItemViewModel? RemovalAsked { get; set; }

    /// <summary>
    /// Gets the question before a participant is removed.
    /// </summary>
    public string RemovalText => RemovalAsked is { } person ? Texts.Format("Main.Moderation.Remove.Text", person.Name) : string.Empty;

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
    /// Gets or sets a value indicating whether the film played to its end, so the button starts it again from the start.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEnded { get; set; }

    /// <summary>
    /// Gets or sets what the start of the film waits for: the seconds of a scheduled start or the readiness of the
    /// participants; empty when it waits for nothing.
    /// </summary>
    [ObservableProperty]
    public partial string StartText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the user gave the film the whole screen: it stays on its side however
    /// the device is held, until the user takes it back.
    /// </summary>
    [ObservableProperty]
    public partial bool IsFullScreen { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user is asked to confirm leaving the session.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLeaveAsked { get; set; }

    /// <summary>
    /// Gets or sets the frame in which the user places a chosen image as their picture, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    public partial AvatarEditorViewModel? AvatarEditor { get; set; }

    /// <summary>
    /// Gets a value indicating whether the user may choose their own picture.
    /// </summary>
    public bool CanChooseAvatar => _features.IsAvailable(PaidFeature.Avatar);

    /// <summary>
    /// Gets the version of the application, as the join screen shows it.
    /// </summary>
    public string VersionText => Texts.Format("About.Version", _version);

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
            _ownAvatar = await LoadOwnAvatarAsync(services);
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
    /// Reads the text the user copied, for the field of the invitation.
    /// </summary>
    /// <returns>The text, or <see langword="null"/> when nothing was copied.</returns>
    public string? ReadClipboard() => _platform.ReadClipboardText();

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
        IsEnded = snapshot.Playback is { State: PlayState.Paused, Cause: PlaybackCause.Ended };
        StartText = DescribeStart(snapshot);
        PositionText = DisplayFormat.Position(player.Position);
        DurationText = DisplayFormat.Position(player.Duration);
        DurationSeconds = Math.Max(player.Duration?.TotalSeconds ?? 0, 1);
        if (!IsDragging)
        {
            PositionSeconds = player.Position?.TotalSeconds ?? 0;
        }
    }

    /// <summary>
    /// Answers the back button of the system: what lies on top goes first, then the film leaves the whole screen, and
    /// then the user is asked whether to leave the session; the session never ends without the answer.
    /// </summary>
    /// <returns><see langword="true"/> when the button was used here.</returns>
    public bool GoBack()
    {
        if (IsHostWarningShown)
        {
            CancelHostWarning();
            return true;
        }

        if (IsHostSetup)
        {
            CancelHostSetup();
            return true;
        }

        if (_connection is null)
        {
            return false;
        }

        if (IsLeaveAsked)
        {
            IsLeaveAsked = false;
        }
        else if (Gallery.IsGridOpen)
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
        else if (IsFullScreen)
        {
            // The film on the whole screen goes back upright first, as in the players people know.
            ToggleFullScreen();
        }
        else
        {
            IsLeaveAsked = true;
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

            // An old invitation is turned away here, before the cameras and the service of the session start for it.
            if (invite.ExpiresAt <= _timeProvider.GetUtcNow())
            {
                throw new SessionJoinException(Texts.Get("Session.Error.InviteExpired"));
            }

            var name = await SaveDisplayNameAsync(services);
            var conference = await PrepareDevicesAsync(services);
            var connection = services.CreateConnection(conference, _current, name);
            await UseConnectionAsync(services, connection);

            await connection.JoinAsync(invite, cancellation.Token);
            Refresh();
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            await FinishAsync(ex is OperationCanceledException ? null : ex.Message);
        }
    }

    /// <summary>
    /// Begins a session hosted on this device: the user is warned about hosting on a phone first, unless they asked
    /// not to be.
    /// </summary>
    /// <returns>A task that completes when the warning or the screen of the session is shown.</returns>
    [RelayCommand]
    private async Task CreateSessionAsync()
    {
        if (_services is not { } services)
        {
            return;
        }

        Error = string.Empty;
        var hidden = await services.Settings.GetAsync(MobileSettings.HOST_WARNING_HIDDEN, CancellationToken.None);
        if (bool.TryParse(hidden, out var isHidden) && isHidden)
        {
            OpenHostSetup();
            return;
        }

        HostWarningHidden = false;
        IsHostWarningShown = true;
    }

    /// <summary>
    /// Goes on past the warning, remembering the wish not to see it again.
    /// </summary>
    /// <returns>A task that completes when the screen of the session is shown.</returns>
    [RelayCommand]
    private async Task ContinueHostWarningAsync()
    {
        IsHostWarningShown = false;
        if (HostWarningHidden && _services is { } services)
        {
            await services.Settings.SetAsync(MobileSettings.HOST_WARNING_HIDDEN, bool.TrueString, CancellationToken.None);
        }

        OpenHostSetup();
    }

    /// <summary>
    /// Stays on the join screen: the warning goes away.
    /// </summary>
    [RelayCommand]
    private void CancelHostWarning()
    {
        IsHostWarningShown = false;
        HostWarningHidden = false;
    }

    /// <summary>
    /// Goes back from the screen of a new session to the join screen.
    /// </summary>
    [RelayCommand]
    private void CancelHostSetup()
    {
        Media.Clear();
        OpenedFiles.Clear();
        Screen = MobileScreen.Join;
    }

    /// <summary>
    /// Starts the session this device hosts and shows what the user chose.
    /// </summary>
    /// <returns>A task that completes when the session runs or could not start.</returns>
    [RelayCommand]
    private async Task StartHostingAsync()
    {
        if (_services is not { } services || IsBusy)
        {
            return;
        }

        if (!Media.HasChoice)
        {
            Media.Error = Texts.Get("Mobile.Host.NothingChosen");
            return;
        }

        Error = string.Empty;
        IsBusy = true;
        try
        {
            var name = await SaveDisplayNameAsync(services);
            var sessionName = string.IsNullOrWhiteSpace(NewSessionName) ? Texts.Get("Main.DefaultSessionName") : NewSessionName.Trim();
            var conference = await PrepareDevicesAsync(services);
            var host = services.CreateHost(conference, _current, _admissions);
            await UseConnectionAsync(services, host);

            await host.StartAsync(sessionName, name, CancellationToken.None);
            IsHost = true;
            await ShowChoiceAsync(host);
            Refresh();
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            await FinishAsync(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Opens the choice of something else to show.
    /// </summary>
    [RelayCommand]
    private void ChangeMedia()
    {
        if (!IsHost)
        {
            return;
        }

        Media.Clear();
        IsMediaChangeOpen = true;
    }

    /// <summary>
    /// Shows the newly chosen film or folder to everybody.
    /// </summary>
    /// <returns>A task that completes when everybody was told.</returns>
    [RelayCommand]
    private async Task ApplyMediaChangeAsync()
    {
        if (_connection is not IHostConnection host || !Media.HasChoice || IsBusy)
        {
            return;
        }

        IsMediaChangeOpen = false;
        IsBusy = true;
        try
        {
            await ShowChoiceAsync(host);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or InvalidDataException)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Shows the invitation into the hosted session: a new link and a new key.
    /// </summary>
    [RelayCommand]
    private void ShowInvitation()
    {
        if (_connection is IHostConnection host)
        {
            Invitation = new InviteViewModel(host.CreateInvite, _platform);
        }
    }

    /// <summary>
    /// Lets the waiting participant in.
    /// </summary>
    [RelayCommand]
    private void Admit() => AnswerAdmission(true);

    /// <summary>
    /// Turns the waiting participant away.
    /// </summary>
    [RelayCommand]
    private void Decline() => AnswerAdmission(false);

    /// <summary>
    /// Removes the participant the host confirmed.
    /// </summary>
    /// <returns>A task that completes when the participant was removed.</returns>
    [RelayCommand]
    private async Task ConfirmRemovalAsync()
    {
        var person = RemovalAsked;
        RemovalAsked = null;
        if (person is null || _connection is not IHostConnection host)
        {
            return;
        }

        try
        {
            await host.RemoveParticipantAsync(person.PeerId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Error = Texts.Get("Main.Error.RemoveFailed");
        }
    }

    /// <summary>
    /// Keeps the participant the host was about to remove.
    /// </summary>
    [RelayCommand]
    private void CancelRemoval() => RemovalAsked = null;

    /// <summary>
    /// Opens the screen of a new hosted session with what was chosen before forgotten.
    /// </summary>
    private void OpenHostSetup()
    {
        var offered = Texts.Get("Main.DefaultSessionName");
        if (string.IsNullOrWhiteSpace(NewSessionName) || NewSessionName == _offeredSessionName)
        {
            NewSessionName = offered;
        }

        _offeredSessionName = offered;
        Media.Clear();
        Screen = MobileScreen.Host;
    }

    /// <summary>
    /// Shows to everybody what the host chose: the film, or the folder.
    /// </summary>
    /// <param name="host">The hosted session.</param>
    /// <returns>A task that completes when everybody was told.</returns>
    private async Task ShowChoiceAsync(IHostConnection host)
    {
        if (Media.FilmPath is { } film)
        {
            await host.ShareMediaAsync(film, CancellationToken.None);
        }
        else if (Media.Folder is { } folder)
        {
            await host.ShowGalleryAsync(folder, Media.StripMetadata, CancellationToken.None);
        }
    }

    /// <summary>
    /// Shows the first participant who waits to come in, or nobody.
    /// </summary>
    private void ShowAdmission()
    {
        var request = _admissions.Current;
        if (request is null)
        {
            Admission = null;
        }
        else if (!ReferenceEquals(Admission?.Request, request))
        {
            Admission = new AdmissionViewModel(request);
        }
    }

    /// <summary>
    /// Answers the participant who waits to come in.
    /// </summary>
    /// <param name="admit">Whether the participant may come in.</param>
    private void AnswerAdmission(bool admit)
    {
        if (Admission is { } shown)
        {
            _admissions.Answer(shown.Request, admit);
        }
    }

    /// <summary>
    /// Switches devices of a participant off (host only); a failure is shown.
    /// </summary>
    /// <param name="peerId">The participant.</param>
    /// <param name="microphone">Whether to switch the microphone off.</param>
    /// <param name="camera">Whether to switch the camera off.</param>
    /// <returns>A task that completes when the request was sent.</returns>
    private async Task SwitchOffParticipantDevicesAsync(PeerId peerId, bool microphone, bool camera)
    {
        if (_connection is not IHostConnection host)
        {
            return;
        }

        try
        {
            await host.SwitchOffParticipantDevicesAsync(peerId, microphone, camera);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Error = Texts.Get("Main.Error.SwitchOffFailed");
        }
    }

    /// <summary>
    /// Asks the host before a participant is removed: coming back takes a new invitation.
    /// </summary>
    /// <param name="peerId">The participant.</param>
    /// <returns>A completed task.</returns>
    private Task AskRemovalAsync(PeerId peerId)
    {
        RemovalAsked = Participants.FirstOrDefault(p => p.PeerId == peerId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Takes the name of the user as the others will see it and remembers it.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <returns>The name.</returns>
    private async Task<string> SaveDisplayNameAsync(IMobileServices services)
    {
        var name = ParticipantInfo.NormalizeDisplayName(DisplayName);
        DisplayName = name;
        await services.Settings.SetAsync(ClientSettings.DISPLAY_NAME, name, CancellationToken.None);

        return name;
    }

    /// <summary>
    /// Asks the system for the camera and the microphone and starts the cameras and voices.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <returns>The cameras and voices.</returns>
    private async Task<IConferenceMedia> PrepareDevicesAsync(IMobileServices services)
    {
        var access = await _platform.RequestCaptureAccessAsync(camera: true, microphone: true);
        var conference = await UseConferenceAsync(services);
        conference.SetMuted(MicrophoneMuted || !access.Microphone, CameraOff || !access.Camera);
        ReadDevices();

        return conference;
    }

    /// <summary>
    /// Makes a new session the session of the moment: the screen follows it, the picture of the user goes into it,
    /// and the system keeps it alive.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="connection">The session.</param>
    /// <returns>A task that completes when the session is in use.</returns>
    private async Task UseConnectionAsync(IMobileServices services, ISessionConnection connection)
    {
        connection.Changed += (_, _) => _dispatcher.Post(Refresh);
        connection.AvatarReceived += (_, update) => _dispatcher.Post(() => ShowAvatar(update));
        _connection = connection;
        if (_ownAvatar is { } avatar)
        {
            _avatars.Apply(new AvatarUpdate(services.Identity.PeerId, avatar, true));
            await connection.SetAvatarAsync(avatar);
        }

        _platform.SetSessionActive(true);
    }

    /// <summary>
    /// Puts the copied invitation into its field.
    /// </summary>
    [RelayCommand]
    private void PasteInvite()
    {
        if (ReadClipboard() is { } text && !string.IsNullOrWhiteSpace(text))
        {
            OpenInvite(text.Trim());
        }
    }

    /// <summary>
    /// Empties the field of the invitation for another one.
    /// </summary>
    [RelayCommand]
    private void ClearInvite()
    {
        InviteLink = string.Empty;
        Error = string.Empty;
    }

    /// <summary>
    /// Gives the film the whole screen on its side, or takes it back upright.
    /// </summary>
    [RelayCommand]
    private void ToggleFullScreen()
    {
        IsFullScreen = !IsFullScreen;
        _platform.HoldLandscape(IsFullScreen);
    }

    /// <summary>
    /// Opens the own picture in its frame as the user left it, with the image it was made from; without one the user
    /// chooses an image first.
    /// </summary>
    /// <returns>A task that completes when the frame is open or the choice was given up.</returns>
    [RelayCommand]
    private async Task EditAvatarAsync()
    {
        if (!CanChooseAvatar || _services is not { } services)
        {
            return;
        }

        StoredAvatar? stored = null;
        try
        {
            stored = await services.Avatars.LoadForEditingAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A kept picture that cannot be read is as good as none: the user chooses an image.
        }

        if (stored is not null && AvatarEditorViewModel.TryOpen(stored.Original, _pictures, stored.Framing) is { } editor)
        {
            AvatarEditor = editor;
            return;
        }

        await PickAvatarAsync();
    }

    /// <summary>
    /// Lets the user choose an image for their picture and opens it in the frame.
    /// </summary>
    /// <returns>A task that completes when the image is chosen or the choice was given up.</returns>
    [RelayCommand]
    private async Task PickAvatarAsync()
    {
        if (!CanChooseAvatar || _storage() is not { } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Texts.Get("Avatar.Pick"),
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });
        if (files.Count == 0)
        {
            return;
        }

        using var file = files[0];
        try
        {
            await using var source = await file.OpenReadAsync();
            var bytes = await ReadLimitedAsync(source, MAX_AVATAR_SOURCE_BYTES);
            AvatarEditor = AvatarEditorViewModel.TryOpen(bytes, _pictures) ?? throw new InvalidDataException(Texts.Get("Avatar.BadImage.Title"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Error = Texts.Get("Avatar.BadImage.Title");
        }
    }

    /// <summary>
    /// Leaves the picture as it was.
    /// </summary>
    [RelayCommand]
    private void CancelAvatar() => AvatarEditor = null;

    /// <summary>
    /// Asks the user whether to leave the session: a single touch of the button must not end it.
    /// </summary>
    [RelayCommand]
    private void AskLeave() => IsLeaveAsked = true;

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
    /// Starts or pauses the film for everybody; a film that played to its end starts again from the start.
    /// </summary>
    [RelayCommand]
    private void TogglePlay()
        => Request(IsEnded
            ? new PlaybackRequest(PlaybackRequestKind.Play, TimeSpan.Zero)
            : new PlaybackRequest(IsPaused ? PlaybackRequestKind.Play : PlaybackRequestKind.Pause, null));

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
        OnPropertyChanged(nameof(VersionText));
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
    /// Takes the picture the user framed: it is kept for later sessions, shown on this screen and sent to the others.
    /// </summary>
    /// <param name="avatar">The circle of the frame as PNG.</param>
    /// <returns>A task that completes when the picture is kept and sent.</returns>
    public async Task ApplyAvatarAsync(byte[] avatar)
    {
        ArgumentNullException.ThrowIfNull(avatar);
        if (AvatarEditor is not { } editor || _services is not { } services)
        {
            return;
        }

        AvatarEditor = null;
        try
        {
            await services.Avatars.SaveAsync(new AvatarSelection(avatar, editor.Original, editor.Framing));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = Texts.Get("Avatar.NotSaved.Title");
            return;
        }

        _ownAvatar = avatar;
        if (_connection is { } connection)
        {
            ShowAvatar(new AvatarUpdate(services.Identity.PeerId, avatar, true));
            await connection.SetAvatarAsync(avatar);
        }
    }

    /// <summary>
    /// Reads the picture the user chose before; a picture that cannot be read is as good as none.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <returns>The picture, or <see langword="null"/>.</returns>
    private static async Task<byte[]?> LoadOwnAvatarAsync(IMobileServices services)
    {
        try
        {
            return await services.Avatars.LoadCurrentAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a chosen file, as long as it is not larger than allowed.
    /// </summary>
    /// <param name="source">The file.</param>
    /// <param name="limit">The most bytes.</param>
    /// <returns>The bytes.</returns>
    /// <exception cref="InvalidDataException">The file is larger.</exception>
    private static async Task<byte[]> ReadLimitedAsync(Stream source, long limit)
    {
        using var copy = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            if (copy.Length + read > limit)
            {
                throw new InvalidDataException(Texts.Get("Avatar.BadImage.Title"));
            }

            copy.Write(buffer, 0, read);
        }

        return copy.ToArray();
    }

    /// <summary>
    /// Claps the clapperboard when somebody else writes in the chat, as the desktop does. The sender hears nothing for
    /// their own message, and reactions are silent: they float over the film and come in handfuls.
    /// </summary>
    private void AnnounceMessage()
    {
        if (Chat.Lines.LastOrDefault() is not { IsFromOther: true } || _messageSound.Value is not { } sound)
        {
            return;
        }

        var now = _timeProvider.GetTimestamp();
        if (_messageSoundPlayed is { } last && _timeProvider.GetElapsedTime(last, now) < _messageSoundInterval)
        {
            return;
        }

        _messageSoundPlayed = now;
        _platform.Sounds.Play(sound);
    }

    /// <summary>
    /// Reads the sound of a new message; a build without it stays silent rather than broken.
    /// </summary>
    /// <returns>The sound, or <see langword="null"/>.</returns>
    private static byte[]? LoadMessageSound()
    {
        try
        {
            return ClapperboardSound.Load();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Words what the start of the film waits for, as the desktop does: a start pressed here comes a few seconds later,
    /// and without a word the button would seem broken.
    /// </summary>
    /// <param name="snapshot">The session.</param>
    /// <returns>The text, empty when the film waits for nothing.</returns>
    private static string DescribeStart(SessionSnapshot snapshot)
    {
        if (snapshot.Playback is { State: PlayState.Paused, Cause: PlaybackCause.WaitingForParticipants })
        {
            return Texts.Get("Main.Waiting.Everybody");
        }

        if (snapshot.Local?.StartsIn is not { } startsIn)
        {
            return string.Empty;
        }

        return Texts.Format("Main.StartsIn", Math.Ceiling(startsIn.TotalSeconds).ToString("0", CultureInfo.InvariantCulture));
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
                tile = new ParticipantItemViewModel(view.Info.PeerId, SwitchOffParticipantDevicesAsync, ChangeVoiceVolume, AskRemovalAsync)
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
            tile.CanModerate = IsHost && !tile.IsLocal;
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
        IsLeaveAsked = false;
        AvatarEditor = null;
        IsHost = false;
        IsMediaChangeOpen = false;
        Invitation = null;
        RemovalAsked = null;
        _admissions.Clear();
        Media.Clear();
        if (IsFullScreen)
        {
            ToggleFullScreen();
        }

        Screen = MobileScreen.Join;
        StartText = string.Empty;
        IsEnded = false;
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
