using NSubstitute;
using Golether.Core.Data.Enums;
using Golether.Core.Enums;
using Golether.Core.Identity;
using Golether.Core.Networking;
using Golether.Core.Session;
using Golether.Localization;
using Golether.Media.Streaming.Gallery;
using Golether.Mobile.Services;
using Golether.Mobile.ViewModels;
using Golether.Security.Admission;
using Golether.Security.Invites;
using Golether.Security.Verification;
using Golether.Session;

namespace Golether.Mobile.Tests;

/// <summary>
/// Tests of a session the phone hosts: the warning, the screen of a new session, and what only the host may do.
/// </summary>
public sealed class HostingTests : IDisposable
{
    /// <summary>
    /// A guest of the session.
    /// </summary>
    private static readonly PeerId _guest = PeerId.Parse(new string('d', 64));

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
    /// Initializes a new instance of the <see cref="HostingTests"/> class.
    /// </summary>
    public HostingTests()
    {
        _platform.IsOnScreen.Returns(true);
        _platform.RequestCaptureAccessAsync(Arg.Any<bool>(), Arg.Any<bool>()).Returns(new CaptureAccess(true, true));

        _model = new MainViewModel(_platform, new ImmediateDispatcher(), () => null, _ => Task.FromResult<IMobileServices>(_services), bytes => bytes);
    }

    /// <inheritdoc />
    public void Dispose() => _services.Dispose();

    /// <summary>
    /// The first time the user is warned before hosting, and may go back.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task CreateSession_WarnsFirst_AndCancelStaysOnTheJoinScreen()
    {
        await _model.StartAsync();

        await _model.CreateSessionCommand.ExecuteAsync(null);
        Assert.True(_model.IsHostWarningShown);
        Assert.False(_model.HostWarningHidden);

        _model.CancelHostWarningCommand.Execute(null);
        Assert.False(_model.IsHostWarningShown);
        Assert.True(_model.IsJoin);
    }

    /// <summary>
    /// Going on without the mark leaves the warning for next time.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task ContinueWarning_WithoutTheMark_KeepsTheWarning()
    {
        await _model.StartAsync();
        await _model.CreateSessionCommand.ExecuteAsync(null);

        await _model.ContinueHostWarningCommand.ExecuteAsync(null);

        Assert.True(_model.IsHostSetup);
        await _services.Settings.DidNotReceive().SetAsync(MobileSettings.HOST_WARNING_HIDDEN, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Going on with the mark remembers it, and the next time the screen of the session opens at once.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task ContinueWarning_WithTheMark_IsNotShownAgain()
    {
        await _model.StartAsync();
        await _model.CreateSessionCommand.ExecuteAsync(null);
        _model.HostWarningHidden = true;

        await _model.ContinueHostWarningCommand.ExecuteAsync(null);
        await _services.Settings.Received().SetAsync(MobileSettings.HOST_WARNING_HIDDEN, bool.TrueString, Arg.Any<CancellationToken>());

        _model.CancelHostSetupCommand.Execute(null);
        _services.Settings.GetAsync(MobileSettings.HOST_WARNING_HIDDEN, Arg.Any<CancellationToken>()).Returns(bool.TrueString);
        await _model.CreateSessionCommand.ExecuteAsync(null);

        Assert.False(_model.IsHostWarningShown);
        Assert.True(_model.IsHostSetup);
        Assert.Equal(Texts.Get("Main.DefaultSessionName"), _model.NewSessionName);
    }

    /// <summary>
    /// Without a film or a folder the session does not start, and the user learns why.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task StartHosting_WithoutAChoice_AsksForOne()
    {
        await OpenSetupAsync();

        await _model.StartHostingCommand.ExecuteAsync(null);

        Assert.Equal(Texts.Get("Mobile.Host.NothingChosen"), _model.Media.Error);
        Assert.Null(_services.Host.StartedAs);
        Assert.True(_model.IsHostSetup);
    }

    /// <summary>
    /// A chosen film is shown to everybody once the session runs, and the screen of the session opens for the host.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task StartHosting_WithAFilm_HostsAndShowsIt()
    {
        await StartHostingAsync(film: "/opened/1/Film.mkv");

        Assert.Equal("Кино у Кати", _services.Host.StartedAs);
        Assert.Equal(["/opened/1/Film.mkv"], _services.Host.Films);
        Assert.True(_model.IsHost);
        Assert.True(_model.IsWatch);
        Assert.NotNull(_services.Prompt);
    }

    /// <summary>
    /// The place of shooting is left out of the pictures of a folder unless the host asks otherwise.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task StartHosting_WithAFolder_LeavesThePlaceOut()
    {
        await OpenSetupAsync();
        _model.Media.Folder = new GalleryFolder("Camera", [new GalleryFile("/opened/2/a.jpg", string.Empty, GalleryItemKind.Picture, 10, false)], 0, 10, false);
        _services.Host.Snapshot = Snapshot();

        await _model.StartHostingCommand.ExecuteAsync(null);

        var shown = Assert.Single(_services.Host.Folders);
        Assert.True(shown.StripMetadata);
        Assert.Equal("Camera", shown.Folder.Title);
    }

    /// <summary>
    /// A session that could not start returns the user to the join screen with the reason, and leaves nothing behind.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task StartHosting_WhenTheStartFails_ShowsTheReason()
    {
        _services.Host.StartFailure = new InvalidOperationException("no network");

        await StartHostingAsync(film: "/opened/1/Film.mkv");

        Assert.True(_model.IsJoin);
        Assert.Equal("no network", _model.Error);
        Assert.False(_model.IsHost);
        Assert.True(_services.Host.IsDisposed);
    }

    /// <summary>
    /// The host may switch the devices of a guest off and remove the guest; the own tile has none of it.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Host_ModeratesGuestsOnly()
    {
        await StartHostingAsync(film: "/opened/1/Film.mkv");

        var guest = _model.Participants.Single(p => p.PeerId == _guest);
        await guest.SwitchOffMicrophoneCommand.ExecuteAsync(null);
        await guest.SwitchOffCameraCommand.ExecuteAsync(null);

        Assert.True(guest.CanModerate);
        Assert.False(_model.Participants.Single(p => p.IsLocal).CanModerate);
        Assert.Equal([(_guest, true, false), (_guest, false, true)], _services.Host.SwitchedOff);
    }

    /// <summary>
    /// A guest is removed only after the host confirmed it, and stays when the host changed their mind.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Remove_AsksTheHostFirst()
    {
        await StartHostingAsync(film: "/opened/1/Film.mkv");
        var guest = _model.Participants.Single(p => p.PeerId == _guest);

        await guest.RemoveCommand.ExecuteAsync(null);
        Assert.Same(guest, _model.RemovalAsked);
        Assert.Equal(Texts.Format("Main.Moderation.Remove.Text", "Олег"), _model.RemovalText);
        _model.CancelRemovalCommand.Execute(null);
        Assert.Empty(_services.Host.Removed);

        await guest.RemoveCommand.ExecuteAsync(null);
        await _model.ConfirmRemovalCommand.ExecuteAsync(null);

        Assert.Equal([_guest], _services.Host.Removed);
        Assert.Null(_model.RemovalAsked);
    }

    /// <summary>
    /// A participant who knocks is shown with the code; the answer of the host lets them in.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Admission_ShowsTheCodeAndTakesTheAnswer()
    {
        await StartHostingAsync(film: "/opened/1/Film.mkv");
        var request = new AdmissionRequest(_guest, "Ира", new VerificationCode([1, 2, 3], 7), false);

        var answer = _services.Prompt!.AskAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(Texts.Format("Admission.Title", "Ира"), _model.Admission?.Title);
        Assert.Equal(4, _model.Admission?.CodeParts.Count);
        Assert.Equal("07", _model.Admission?.CodeParts[3]);

        _model.AdmitCommand.Execute(null);

        Assert.True(await answer);
        Assert.Null(_model.Admission);
    }

    /// <summary>
    /// The invitation gives a link and a key at once, and copying puts the link into the clipboard.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Invitation_GivesTheLinkAndTheKey()
    {
        await StartHostingAsync(film: "/opened/1/Film.mkv");

        _model.ShowInvitationCommand.Execute(null);
        var invitation = Assert.IsType<InviteViewModel>(_model.Invitation);
        invitation.CopyLinkCommand.Execute(null);

        Assert.True(invitation.HasInvite);
        Assert.StartsWith(Invite.WEB_LINK_PREFIX, invitation.Link, StringComparison.Ordinal);
        Assert.NotEmpty(invitation.Key);
        _platform.Received().CopyText(invitation.Link);
    }

    /// <summary>
    /// Ending the session as the host closes it for good and leaves the host tools.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Leave_AsTheHost_EndsTheSession()
    {
        await StartHostingAsync(film: "/opened/1/Film.mkv");

        await _model.LeaveCommand.ExecuteAsync(null);

        Assert.True(_services.Host.IsDisposed);
        Assert.False(_model.IsHost);
        Assert.True(_model.IsJoin);
    }

    /// <summary>
    /// The back button leaves the screen of a new session for the join screen.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task GoBack_FromTheSetup_ReturnsToTheJoinScreen()
    {
        await OpenSetupAsync();

        Assert.True(_model.GoBack());
        Assert.True(_model.IsJoin);
    }

    /// <summary>
    /// A participant who came by an invitation hosts nothing and moderates nobody.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Participant_HasNoHostTools()
    {
        await _model.StartAsync();
        var host = PeerId.Parse(new string('c', 64));
        _services.Connection.Snapshot = new SessionSnapshot
        {
            IsHost = false,
            State = SessionState.Active,
            SessionName = "Киновечер",
            HostPeerId = host,
            Participants =
            [
                new ParticipantView(new ParticipantInfo(host, "Хост", true), null, false),
                new ParticipantView(new ParticipantInfo(_services.Identity.PeerId, "Катя", false), null, true),
                new ParticipantView(new ParticipantInfo(_guest, "Олег", false), null, false),
            ],
        };
        _model.DisplayName = "Катя";
        _model.InviteLink = new Invite
        {
            HostPeerId = host,
            HostName = "Хост",
            SessionName = "Киновечер",
            Endpoints = [PeerEndpoint.Parse("192.168.1.10:47800")],
            Token = Invite.CreateToken(),
            ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(2_000_000_000),
        }.ToLink();

        await _model.JoinCommand.ExecuteAsync(null);
        _model.ShowInvitationCommand.Execute(null);

        Assert.True(_model.IsWatch);
        Assert.False(_model.IsHost);
        Assert.Null(_model.Invitation);
        Assert.All(_model.Participants, p => Assert.False(p.CanModerate));
    }

    /// <summary>
    /// Opens the screen of a new session past the warning.
    /// </summary>
    /// <returns>A task that completes when the screen is shown.</returns>
    private async Task OpenSetupAsync()
    {
        await _model.StartAsync();
        _services.Settings.GetAsync(MobileSettings.HOST_WARNING_HIDDEN, Arg.Any<CancellationToken>()).Returns(bool.TrueString);
        await _model.CreateSessionCommand.ExecuteAsync(null);
        _model.DisplayName = "Катя";
        _model.NewSessionName = "Кино у Кати";
    }

    /// <summary>
    /// Starts a session that shows a film.
    /// </summary>
    /// <param name="film">The path of the film.</param>
    /// <returns>A task that completes when the session runs.</returns>
    private async Task StartHostingAsync(string film)
    {
        await OpenSetupAsync();
        _model.Media.FilmPath = film;
        _services.Host.Snapshot = Snapshot();

        await _model.StartHostingCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Makes the state of the session of this phone with one guest.
    /// </summary>
    /// <returns>The state.</returns>
    private SessionSnapshot Snapshot() => new()
    {
        IsHost = true,
        State = SessionState.Active,
        SessionName = "Кино у Кати",
        HostPeerId = _services.Identity.PeerId,
        Participants =
        [
            new ParticipantView(new ParticipantInfo(_services.Identity.PeerId, "Катя", true), null, true),
            new ParticipantView(new ParticipantInfo(_guest, "Олег", false), null, false),
        ],
    };
}
