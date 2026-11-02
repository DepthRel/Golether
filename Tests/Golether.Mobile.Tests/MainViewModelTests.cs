using NSubstitute;
using Golether.Client.Services;
using Golether.Core.Data.Enums;
using Golether.Core.Enums;
using Golether.Core.Identity;
using Golether.Core.Networking;
using Golether.Core.Playback;
using Golether.Core.Session;
using Golether.Localization;
using Golether.Mobile.Services;
using Golether.Mobile.ViewModels;
using Golether.Security.Invites;
using Golether.Security.Verification;
using Golether.Session;
using Golether.Sync.Engine;

namespace Golether.Mobile.Tests;

/// <summary>
/// Tests of <see cref="MainViewModel"/>: the way through the screens and what the system and the session do to them.
/// </summary>
public sealed class MainViewModelTests : IDisposable
{
    /// <summary>
    /// The host.
    /// </summary>
    private static readonly PeerId _host = PeerId.Parse(new string('c', 64));

    /// <summary>
    /// This device.
    /// </summary>
    private static readonly PeerId _self = PeerId.Parse(new string('a', 64));

    /// <summary>
    /// The system.
    /// </summary>
    private readonly IMobilePlatform _platform = Substitute.For<IMobilePlatform>();

    /// <summary>
    /// The services.
    /// </summary>
    private readonly StubServices _services = new();

    /// <summary>
    /// The model.
    /// </summary>
    private readonly MainViewModel _model;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModelTests"/> class.
    /// </summary>
    public MainViewModelTests()
    {
        _platform.IsOnScreen.Returns(true);
        _platform.RequestCaptureAccessAsync(Arg.Any<bool>(), Arg.Any<bool>()).Returns(new CaptureAccess(true, true));

        _model = new MainViewModel(_platform, new ImmediateDispatcher(), () => null, _ => Task.FromResult<IMobileServices>(_services), bytes => bytes);
    }

    /// <inheritdoc />
    public void Dispose() => _services.Dispose();

    /// <summary>
    /// The application opens on the join screen with the name it remembers.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Start_ShowsTheJoinScreenWithTheStoredName()
    {
        _services.Settings.GetAsync(ClientSettings.DISPLAY_NAME, Arg.Any<CancellationToken>()).Returns("Катя");

        await _model.StartAsync();

        Assert.True(_model.IsJoin);
        Assert.Equal("Катя", _model.DisplayName);
    }

    /// <summary>
    /// A player that could not start is told, and the screen still opens.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Start_WhenTheServicesFail_ShowsTheReason()
    {
        var model = new MainViewModel(_platform, new ImmediateDispatcher(), () => null, _ => throw new InvalidOperationException("no player"));

        await model.StartAsync();

        Assert.True(model.IsJoin);
        Assert.Equal("no player", model.Error);
    }

    /// <summary>
    /// Text that is no invitation leaves the user on the join screen with a reason.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Join_WithABrokenLink_ReturnsWithAReason()
    {
        await _model.StartAsync();
        _model.DisplayName = "Катя";
        _model.InviteLink = "это не приглашение";

        await _model.JoinCommand.ExecuteAsync(null);

        Assert.True(_model.IsJoin);
        Assert.NotEmpty(_model.Error);
        Assert.False(_services.Connection.IsDisposed);
    }

    /// <summary>
    /// The screen follows the session: the code for the host first, the film when the host let the device in.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Join_FollowsTheStateOfTheSession()
    {
        await JoinAsync(SessionState.AwaitingApproval, new VerificationCode([1, 2, 3], 7));

        Assert.True(_model.IsApproval);
        Assert.Equal(4, _model.VerificationParts.Count);
        Assert.Equal("07", _model.VerificationParts[3]);

        _services.Connection.Snapshot = Snapshot(SessionState.Active);
        _services.Connection.RaiseChanged();

        Assert.True(_model.IsWatch);
        Assert.Equal(2, _model.Participants.Count);
    }

    /// <summary>
    /// The same code does not rebuild the words on every refresh: they would blink.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Refresh_KeepsTheShownVerificationCode()
    {
        await JoinAsync(SessionState.AwaitingApproval, new VerificationCode([1, 2, 3], 7));
        var shown = _model.VerificationParts;

        _model.Refresh();

        Assert.Same(shown, _model.VerificationParts);
    }

    /// <summary>
    /// On the way back the film stays on the screen with the state of the connection over it.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Refresh_OnTheWayBack_StaysOnTheFilm()
    {
        await JoinAsync(SessionState.Active);
        _services.Connection.IsReconnecting = true;
        _services.Connection.ReconnectMessage = "ждём";
        _services.Connection.Snapshot = Snapshot(SessionState.Ended);

        _model.Refresh();

        Assert.True(_model.IsWatch);
        Assert.Equal("ждём", _model.ReconnectMessage);
    }

    /// <summary>
    /// A session that ended takes the user to the join screen with the reason, and what it held is gone.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Refresh_WhenTheSessionEnded_ReturnsWithTheReason()
    {
        await JoinAsync(SessionState.Active);
        _services.Connection.EndReason = "хост завершил сессию";
        _services.Connection.Snapshot = Snapshot(SessionState.Ended);

        _model.Refresh();

        Assert.True(_model.IsJoin);
        Assert.Equal("хост завершил сессию", _model.Error);
        Assert.Empty(_model.Participants);
        Assert.True(_services.Connection.IsDisposed);
        _platform.Received().SetSessionActive(false);
    }

    /// <summary>
    /// Leaving closes the session without a word of failure.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Leave_ClosesTheSession()
    {
        await JoinAsync(SessionState.Active);

        await _model.LeaveCommand.ExecuteAsync(null);

        Assert.True(_model.IsJoin);
        Assert.Empty(_model.Error);
        Assert.True(_services.Connection.IsDisposed);
    }

    /// <summary>
    /// Outside a session the back button is the business of the system.
    /// </summary>
    [Fact]
    public void GoBack_OutsideASession_IsLeftToTheSystem() => Assert.False(_model.GoBack());

    /// <summary>
    /// Inside a session the back button asks whether to leave, and a second press takes the question back: the
    /// session never ends without the answer.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task GoBack_InASession_AsksToLeave()
    {
        await JoinAsync(SessionState.Active);

        Assert.True(_model.GoBack());

        Assert.True(_model.IsLeaveAsked);
        Assert.True(_model.IsWatch);

        Assert.True(_model.GoBack());

        Assert.False(_model.IsLeaveAsked);
        Assert.True(_model.IsWatch);
        Assert.False(_services.Connection.IsDisposed);
    }

    /// <summary>
    /// The red button only asks; the answer leaves the session and takes the question away.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task AskLeave_LeavesOnlyWithTheAnswer()
    {
        await JoinAsync(SessionState.Active);

        _model.AskLeaveCommand.Execute(null);

        Assert.True(_model.IsLeaveAsked);
        Assert.False(_services.Connection.IsDisposed);

        await _model.LeaveCommand.ExecuteAsync(null);

        Assert.False(_model.IsLeaveAsked);
        Assert.True(_model.IsJoin);
        Assert.True(_services.Connection.IsDisposed);
    }

    /// <summary>
    /// A call takes the microphone and gives it back when it ends; the sound of the film goes and comes with it.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task AudioInterrupted_TakesTheMicrophoneUntilTheCallEnds()
    {
        await JoinAsync(SessionState.Active);

        _platform.AudioInterrupted += Raise.Event<EventHandler<bool>>(_platform, true);
        Assert.True(_model.MicrophoneMuted);

        _platform.AudioInterrupted += Raise.Event<EventHandler<bool>>(_platform, false);
        Assert.False(_model.MicrophoneMuted);
    }

    /// <summary>
    /// A microphone the user switched off stays off after a call.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task AudioInterrupted_LeavesAMutedMicrophoneOff()
    {
        await JoinAsync(SessionState.Active);
        await _model.ToggleMicrophoneCommand.ExecuteAsync(null);

        _platform.AudioInterrupted += Raise.Event<EventHandler<bool>>(_platform, true);
        _platform.AudioInterrupted += Raise.Event<EventHandler<bool>>(_platform, false);

        Assert.True(_model.MicrophoneMuted);
    }

    /// <summary>
    /// A microphone the system does not allow stays off, and the user is told where to allow it.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task ToggleMicrophone_WithoutPermission_Explains()
    {
        await JoinAsync(SessionState.Active);
        await _model.ToggleMicrophoneCommand.ExecuteAsync(null);
        _platform.RequestCaptureAccessAsync(false, true).Returns(new CaptureAccess(false, false));

        await _model.ToggleMicrophoneCommand.ExecuteAsync(null);

        Assert.True(_model.MicrophoneMuted);
        Assert.Equal(Texts.Get("Mobile.Access.Microphone"), _model.Error);
    }

    /// <summary>
    /// A camera that stopped while the application is on the screen is switched off, and the user is told.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task CameraFailed_OnTheScreen_SwitchesTheCameraOff()
    {
        await JoinAsync(SessionState.Active);

        _services.Conference.CameraFailed += Raise.Event();

        Assert.True(_model.CameraOff);
        Assert.Equal(Texts.Get("Mobile.Camera.Failed"), _model.Error);
    }

    /// <summary>
    /// Off the screen the system takes the camera from every application: it comes back with the application.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task CameraFailed_OffTheScreen_IsNoFailure()
    {
        await JoinAsync(SessionState.Active);
        _platform.IsOnScreen.Returns(false);

        _services.Conference.CameraFailed += Raise.Event();

        Assert.False(_model.CameraOff);
        Assert.Empty(_model.Error);
    }

    /// <summary>
    /// A network that came back hurries the way to the session.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task NetworkReturned_HurriesTheWayBack()
    {
        await JoinAsync(SessionState.Active);

        _platform.NetworkReturned += Raise.Event();

        Assert.Equal(1, _services.Connection.Retries);
    }

    /// <summary>
    /// The picture a participant chose reaches the tile, also when the tile appears after the picture.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Avatar_ReachesTheTile()
    {
        await JoinAsync(SessionState.Active);
        var late = PeerId.Parse(new string('d', 64));

        _services.Connection.RaiseAvatar(new AvatarUpdate(_host, [1], false));
        _services.Connection.RaiseAvatar(new AvatarUpdate(late, [2], false));
        _services.Connection.Snapshot = Snapshot(SessionState.Active, new ParticipantView(new ParticipantInfo(late, "Миша", false), null, false));
        _model.Refresh();

        Assert.NotNull(_model.Participants.Single(p => p.PeerId == _host).AvatarImage);
        Assert.NotNull(_model.Participants.Single(p => p.PeerId == late).AvatarImage);
    }

    /// <summary>
    /// Play goes to the host as a request: the film moves for everybody or for nobody.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task TogglePlay_AsksTheHost()
    {
        await JoinAsync(SessionState.Active);

        _model.TogglePlayCommand.Execute(null);

        Assert.Equal(PlaybackRequestKind.Play, Assert.Single(_services.Connection.Requests).Kind);
    }

    /// <summary>
    /// An invitation from a link does not disturb a running session.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task OpenInvite_InASession_IsIgnored()
    {
        await JoinAsync(SessionState.Active);
        var before = _model.InviteLink;

        _model.OpenInvite("другое");

        Assert.Equal(before, _model.InviteLink);
    }

    /// <summary>
    /// A message the user has read goes away.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task ClearError_TakesTheMessageAway()
    {
        await JoinAsync(SessionState.Active);
        _services.Conference.CameraFailed += Raise.Event();

        _model.ClearErrorCommand.Execute(null);

        Assert.Empty(_model.Error);
    }

    /// <summary>
    /// An invitation whose time is over is turned away on the join screen, before the session and its service start:
    /// a service started and stopped at once would end the application.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Join_WithAnExpiredInvitation_StartsNothing()
    {
        await _model.StartAsync();
        _model.DisplayName = "Катя";
        _model.InviteLink = new Invite
        {
            HostPeerId = _host,
            HostName = "Хост",
            SessionName = "Киновечер",
            Endpoints = [PeerEndpoint.Parse("192.168.1.10:47800")],
            Token = Invite.CreateToken(),
            ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(1_000_000_000),
        }.ToLink();

        await _model.JoinCommand.ExecuteAsync(null);

        Assert.True(_model.IsJoin);
        Assert.Equal(Texts.Get("Session.Error.InviteExpired"), _model.Error);
        _platform.DidNotReceive().SetSessionActive(Arg.Any<bool>());
        await _platform.DidNotReceive().RequestCaptureAccessAsync(Arg.Any<bool>(), Arg.Any<bool>());
    }

    /// <summary>
    /// The paste button takes the text of the system clipboard; the cross empties the field and the message about it.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task PasteAndClear_FillAndEmptyTheInvitation()
    {
        await _model.StartAsync();
        _platform.ReadClipboardText().Returns("  golether://join/v1/abc \n");

        _model.PasteInviteCommand.Execute(null);

        Assert.Equal("golether://join/v1/abc", _model.InviteLink);

        _model.Error = "старая ошибка";
        _model.ClearInviteCommand.Execute(null);

        Assert.Empty(_model.InviteLink);
        Assert.Empty(_model.Error);
    }

    /// <summary>
    /// An empty clipboard leaves the field as it was.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Paste_WithAnEmptyClipboard_KeepsTheField()
    {
        await _model.StartAsync();
        _model.InviteLink = "уже было";
        _platform.ReadClipboardText().Returns((string?)null);

        _model.PasteInviteCommand.Execute(null);

        Assert.Equal("уже было", _model.InviteLink);
    }

    /// <summary>
    /// The full-screen button holds the film on its side until the user takes it back: turning the device does not end
    /// it, the back button does before it asks to leave, and so does the end of the session.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task FullScreen_StaysUntilTheUserTakesItBack()
    {
        await JoinAsync(SessionState.Active);

        _model.ToggleFullScreenCommand.Execute(null);
        _model.FitSubtitles(upright: false);
        _model.FitSubtitles(upright: true);

        Assert.True(_model.IsFullScreen);
        _platform.Received(1).HoldLandscape(true);
        _platform.DidNotReceive().HoldLandscape(false);

        Assert.True(_model.GoBack());

        Assert.False(_model.IsFullScreen);
        Assert.False(_model.IsLeaveAsked);
        _platform.Received(1).HoldLandscape(false);

        _model.ToggleFullScreenCommand.Execute(null);
        await _model.LeaveCommand.ExecuteAsync(null);

        Assert.False(_model.IsFullScreen);
        _platform.Received(2).HoldLandscape(false);
    }

    /// <summary>
    /// The picture the user chose before goes into the session with the join, and the own tile shows it at once.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Join_SendsTheStoredPicture()
    {
        byte[] avatar = [0x89, 0x50, 0x4E, 0x47];
        _services.Avatars.LoadCurrentAsync().Returns(avatar);

        await JoinAsync(SessionState.Active);
        _services.Connection.Snapshot = Snapshot(SessionState.Active) with
        {
            Participants =
            [
                new ParticipantView(new ParticipantInfo(_host, "Хост", true), null, false),
                new ParticipantView(new ParticipantInfo(_services.Identity.PeerId, "Катя", false), null, true),
            ],
        };
        _model.Refresh();

        Assert.Same(avatar, _services.Connection.Avatar);
        Assert.Same(avatar, _model.Participants.Single(p => p.IsLocal).AvatarImage);
    }

    /// <summary>
    /// A film that played to its end starts again from the start, as on the desktop.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task TogglePlay_AfterTheEnd_StartsFromTheStart()
    {
        await JoinAsync(SessionState.Active);
        _services.Connection.Snapshot = Snapshot(SessionState.Active) with
        {
            Playback = new PlaybackState
            {
                State = PlayState.Paused,
                Position = TimeSpan.FromMinutes(90),
                ReferenceTime = 0,
                Version = 3,
                Origin = _host,
                Cause = PlaybackCause.Ended,
            },
        };
        _model.Refresh();

        _model.TogglePlayCommand.Execute(null);

        Assert.True(_model.IsEnded);
        var request = Assert.Single(_services.Connection.Requests);
        Assert.Equal(PlaybackRequestKind.Play, request.Kind);
        Assert.Equal(TimeSpan.Zero, request.Position);
    }

    /// <summary>
    /// A start pressed here comes a few seconds later: the screen counts them, and says when the participants are
    /// waited for.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Refresh_TellsWhatTheStartWaitsFor()
    {
        await JoinAsync(SessionState.Active);
        Assert.Empty(_model.StartText);

        _services.Connection.Snapshot = Snapshot(SessionState.Active) with
        {
            Local = new FollowerStatus(default, null, TimeSpan.Zero, TimeSpan.FromSeconds(2.3)),
        };
        _model.Refresh();

        Assert.Equal(Texts.Format("Main.StartsIn", "3"), _model.StartText);

        _services.Connection.Snapshot = Snapshot(SessionState.Active) with
        {
            Playback = new PlaybackState
            {
                State = PlayState.Paused,
                Position = TimeSpan.Zero,
                ReferenceTime = 0,
                Version = 1,
                Origin = _host,
                Cause = PlaybackCause.WaitingForParticipants,
            },
        };
        _model.Refresh();

        Assert.Equal(Texts.Get("Main.Waiting.Everybody"), _model.StartText);

        await _model.LeaveCommand.ExecuteAsync(null);

        Assert.Empty(_model.StartText);
    }

    /// <summary>
    /// Describes a session.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="others">The participants besides the host and this device.</param>
    /// <returns>The state of the session.</returns>
    private static SessionSnapshot Snapshot(SessionState state, params ParticipantView[] others) => new()
    {
        IsHost = false,
        State = state,
        SessionName = "Киновечер",
        HostPeerId = _host,
        Participants =
        [
            new ParticipantView(new ParticipantInfo(_host, "Хост", true), null, false),
            new ParticipantView(new ParticipantInfo(_self, "Катя", false), null, true),
            .. others,
        ],
    };

    /// <summary>
    /// Goes through the join screen into a session.
    /// </summary>
    /// <param name="state">The state of the session.</param>
    /// <param name="code">The verification code, or <see langword="null"/>.</param>
    /// <returns>A task that completes when the screen shows the session.</returns>
    private async Task JoinAsync(SessionState state, VerificationCode? code = null)
    {
        await _model.StartAsync();
        _services.Connection.Snapshot = Snapshot(state) with { VerificationCode = code };
        _model.DisplayName = "Катя";
        _model.InviteLink = new Invite
        {
            HostPeerId = _host,
            HostName = "Хост",
            SessionName = "Киновечер",
            Endpoints = [PeerEndpoint.Parse("192.168.1.10:47800")],
            Token = Invite.CreateToken(),
            ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(2_000_000_000),
        }.ToLink();

        await _model.JoinCommand.ExecuteAsync(null);
        Assert.Empty(_model.Error);
    }
}
