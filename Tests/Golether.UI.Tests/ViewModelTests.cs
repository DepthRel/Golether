using Golether.Components.Catalog;
using Golether.Components.Installation;
using Golether.Core.Data.Stores;
using Golether.Core.Identity;
using Golether.Core.Media;
using Golether.Core.Networking;
using Golether.Core.Playback;
using Golether.Core.Session;
using Golether.Media.Conference;
using Golether.Security.Invites;
using Golether.Session;
using Golether.Sync.Engine;
using Golether.Sync.Protocol;
using Golether.UI.Services;
using Golether.UI.ViewModels;
using Golether.UI.ViewModels.Dialogs;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of the view models and display formatting.
/// </summary>
public sealed class ViewModelTests
{
    /// <summary>
    /// The host device.
    /// </summary>
    private static readonly PeerId Host = PeerId.Parse(new string('a', 64));

    /// <summary>
    /// A participant.
    /// </summary>
    private static readonly PeerId Guest = PeerId.Parse(new string('b', 64));

    /// <summary>
    /// The session service substitute.
    /// </summary>
    private readonly ISessionService _session = Substitute.For<ISessionService>();

    /// <summary>
    /// The dialog service substitute.
    /// </summary>
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    /// <summary>
    /// The settings substitute.
    /// </summary>
    private readonly ISettingsStore _settings = Substitute.For<ISettingsStore>();

    /// <summary>
    /// The component service substitute: nothing installed, both installable.
    /// </summary>
    private readonly IComponentService _components = CreateComponents();

    /// <summary>
    /// A missing component can be installed from the view model; progress and the new state are shown.
    /// </summary>
    [Fact]
    public async Task Component_InstallsAndRefreshes()
    {
        var viewModel = CreateViewModel();
        var video = viewModel.VideoComponent;
        Assert.False(video.IsAvailable);
        Assert.True(video.CanInstall);
        Assert.Equal("Установить (29 МБ)", video.InstallText);
        Assert.True(viewModel.HasMissingComponents);

        _components.InstallAsync(ComponentId.Video, Arg.Any<IProgress<InstallProgress>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<IProgress<InstallProgress>>().Report(new InstallProgress(InstallStage.Downloading, 10, 20));
                _components.GetStatus(ComponentId.Video).Returns(new ComponentStatus(ComponentId.Video, ComponentSource.Installed, "libmpv-2.dll", null, null));
                return Task.CompletedTask;
            });

        await video.InstallCommand.ExecuteAsync(null);

        Assert.True(video.IsAvailable);
        Assert.False(video.CanInstall);
        Assert.False(video.IsInstalling);
        Assert.Equal("установлен", video.StatusText);
    }

    /// <summary>
    /// An installation error is shown to the user and the button comes back.
    /// </summary>
    [Fact]
    public async Task Component_ShowsInstallErrors()
    {
        _components.InstallAsync(ComponentId.Conference, Arg.Any<IProgress<InstallProgress>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ComponentInstallException("нет сети"));
        var viewModel = CreateViewModel();

        await viewModel.ConferenceComponent.InstallCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ShowErrorAsync(viewModel.ConferenceComponent.Title, "нет сети");
        Assert.True(viewModel.ConferenceComponent.CanInstall);
    }

    /// <summary>
    /// The refresh maps the snapshot: participants, timeline, synchronization chip.
    /// </summary>
    [Fact]
    public void Refresh_MapsSnapshot()
    {
        _session.GetSnapshot().Returns(Snapshot(PlayState.Playing, TimeSpan.FromMilliseconds(-180)));
        var viewModel = CreateViewModel();

        viewModel.Refresh();

        Assert.True(viewModel.IsInSession);
        Assert.True(viewModel.IsHost);
        Assert.True(viewModel.IsPlaying);
        Assert.Equal(2, viewModel.Participants.Count);
        Assert.Equal("Марина", viewModel.Participants[1].Name);
        Assert.Equal("1 240 мс", viewModel.Participants[1].PingText.Replace(' ', ' '));
        Assert.Equal(IndicatorLevel.Warning, viewModel.Participants[1].PingLevel);
        Assert.Equal("−180 мс", viewModel.Participants[1].DriftText);
        Assert.Equal(0.5, viewModel.Participants[1].TimelineFraction!.Value, 3);
        Assert.Equal("Синхронно · Δ ≤ 180 мс", viewModel.SyncText);
        Assert.Equal(7200, viewModel.DurationSeconds);
        Assert.Equal("1:00:00", viewModel.PositionText);
        Assert.Contains("64", viewModel.MediaCaption, StringComparison.Ordinal);
    }

    /// <summary>
    /// Participants that left disappear from the list.
    /// </summary>
    [Fact]
    public void Refresh_RemovesParticipantsThatLeft()
    {
        _session.GetSnapshot().Returns(Snapshot(PlayState.Paused, TimeSpan.Zero));
        var viewModel = CreateViewModel();
        viewModel.Refresh();

        _session.GetSnapshot().Returns(Snapshot(PlayState.Paused, TimeSpan.Zero) with { Participants = [Local()] });
        viewModel.Refresh();

        Assert.Single(viewModel.Participants);
    }

    /// <summary>
    /// Pause sends the current frame; play lets the host schedule the start.
    /// </summary>
    [Fact]
    public async Task TogglePlay_SendsIntent()
    {
        _session.GetSnapshot().Returns(Snapshot(PlayState.Playing, TimeSpan.Zero));
        var viewModel = CreateViewModel();
        viewModel.Refresh();

        await viewModel.TogglePlayCommand.ExecuteAsync(null);
        await _session.Received(1).RequestAsync(new PlaybackRequest(PlaybackRequestKind.Pause, TimeSpan.FromHours(1)), Arg.Any<CancellationToken>());

        _session.GetSnapshot().Returns(Snapshot(PlayState.Paused, TimeSpan.Zero));
        viewModel.Refresh();
        await viewModel.TogglePlayCommand.ExecuteAsync(null);
        await _session.Received(1).RequestAsync(new PlaybackRequest(PlaybackRequestKind.Play, null), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A failed join shows the reason and stores the normalized name.
    /// </summary>
    [Fact]
    public async Task Join_ShowsErrors()
    {
        _session.JoinAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new SessionJoinException("Ведущий отклонил запрос."));
        var viewModel = CreateViewModel();
        viewModel.DisplayName = "  Марина\n";
        viewModel.InviteLink = "golether://join/v1/x";

        await viewModel.JoinCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ShowErrorAsync(Arg.Any<string>(), "Ведущий отклонил запрос.");
        await _settings.Received(1).SetAsync(MainWindowViewModel.DisplayNameSetting, "Марина", Arg.Any<CancellationToken>());
        Assert.False(viewModel.IsBusy);
    }

    /// <summary>
    /// A shared file is passed to the session; a participant file that differs is reported.
    /// </summary>
    [Fact]
    public async Task PickFile_SharesOrUsesLocalCopy()
    {
        _dialogs.PickMediaFileAsync().Returns("movie.mkv");
        var viewModel = CreateViewModel();

        _session.IsHost.Returns(true);
        await viewModel.PickFileCommand.ExecuteAsync(null);
        await _session.Received(1).ShareMediaAsync("movie.mkv", Arg.Any<CancellationToken>());

        _session.IsHost.Returns(false);
        _session.UseLocalCopyAsync("movie.mkv", Arg.Any<CancellationToken>()).Returns(false);
        await viewModel.PickFileCommand.ExecuteAsync(null);
        await _dialogs.Received(1).ShowErrorAsync("Другой файл", Arg.Any<string>());
    }

    /// <summary>
    /// The invitation dialog validates the extra address and copies the link.
    /// </summary>
    [Fact]
    public async Task InviteDialog_ValidatesAndCopies()
    {
        _session.CreateInvite(Arg.Any<IReadOnlyList<PeerEndpoint>>(), Arg.Any<TimeSpan>()).Returns(new Invite
        {
            HostPeerId = Host,
            HostName = "Вы",
            SessionName = "Вечер",
            Endpoints = [PeerEndpoint.Parse("192.168.1.2:47800")],
            Token = Invite.CreateToken(),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
        });
        var dialog = new InviteDialogViewModel(_session, _dialogs) { ExtraAddress = "not an address" };

        dialog.Regenerate();
        Assert.Empty(dialog.Link);
        Assert.Contains("хост:порт", dialog.Message, StringComparison.Ordinal);

        dialog.ExtraAddress = "203.0.113.24:47800";
        dialog.Regenerate();
        Assert.StartsWith(Invite.LinkPrefix, dialog.Link, StringComparison.Ordinal);
        _session.Received(1).CreateInvite(Arg.Is<IReadOnlyList<PeerEndpoint>>(e => e.Single() == PeerEndpoint.Parse("203.0.113.24:47800")), TimeSpan.FromMinutes(15));

        await dialog.CopyCommand.ExecuteAsync(null);
        await _dialogs.Received(1).CopyTextAsync(dialog.Link);
    }

    /// <summary>
    /// Values are formatted in Russian conventions.
    /// </summary>
    [Fact]
    public void DisplayFormat_FormatsValues()
    {
        Assert.Equal("+90 мс", DisplayFormat.Drift(TimeSpan.FromMilliseconds(90)));
        Assert.Equal("−1,8 с", DisplayFormat.Drift(TimeSpan.FromMilliseconds(-1800)));
        Assert.Equal("—", DisplayFormat.Ping(null));
        Assert.Equal("64,2 ГБ", DisplayFormat.Size((long)(64.2 * 1024 * 1024 * 1024)));
        Assert.Equal("2:46:02", DisplayFormat.Position(new TimeSpan(2, 46, 2)));
        Assert.Equal(IndicatorLevel.Critical, DisplayFormat.BufferLevel(TimeSpan.FromSeconds(30), buffering: true));
        Assert.Equal(IndicatorLevel.Good, DisplayFormat.PingLevel(310));
    }

    /// <summary>
    /// Creates the view model with the substitutes.
    /// </summary>
    /// <returns>The view model.</returns>
    private MainWindowViewModel CreateViewModel()
        => new(_session, _dialogs, _settings, new InlineDispatcher(), _ => throw new NotSupportedException(), "A249-B9CC-3AAB-52AC",
            new UnavailableConferenceMedia("нет"),
            _components);

    /// <summary>
    /// Creates the component service substitute.
    /// </summary>
    /// <returns>The substitute.</returns>
    private static IComponentService CreateComponents()
    {
        var components = Substitute.For<IComponentService>();
        foreach (var id in new[] { ComponentId.Video, ComponentId.Conference })
        {
            components.GetStatus(id).Returns(new ComponentStatus(id, ComponentSource.Missing, null, ComponentCatalog.Find(id, "win-x64"), null));
        }

        return components;
    }

    /// <summary>
    /// Returns the local participant view.
    /// </summary>
    /// <returns>The view.</returns>
    private static ParticipantView Local()
        => new(new ParticipantInfo(Host, "Вы", true), new ParticipantStatus { PeerId = Host, Position = TimeSpan.FromHours(1) }, true);

    /// <summary>
    /// Builds a host snapshot with one remote participant.
    /// </summary>
    /// <param name="state">The playback mode.</param>
    /// <param name="guestDrift">The drift of the participant.</param>
    /// <returns>The snapshot.</returns>
    private static SessionSnapshot Snapshot(PlayState state, TimeSpan guestDrift)
    {
        var player = new PlayerSnapshot(true, TimeSpan.FromHours(1), TimeSpan.FromHours(2), state != PlayState.Playing, false, TimeSpan.FromSeconds(20), 1.0);
        return new SessionSnapshot
        {
            IsHost = true,
            State = SessionState.Active,
            SessionName = "Вечер кино",
            HostPeerId = Host,
            Participants =
            [
                Local(),
                new ParticipantView(
                    new ParticipantInfo(Guest, "Марина", false),
                    new ParticipantStatus { PeerId = Guest, Position = TimeSpan.FromHours(1), Drift = guestDrift, RoundTripMilliseconds = 1240, CacheAhead = TimeSpan.FromSeconds(12) },
                    false),
            ],
            Playback = PlaybackState.Initial(Host, 0) with { State = state, Position = TimeSpan.FromHours(1) },
            Media = new MediaDescriptor { FileName = "Dune.mkv", Length = (long)(64.2 * 1024 * 1024 * 1024), QuickId = new string('c', 64) },
            Local = new FollowerStatus(player, TimeSpan.FromHours(1), TimeSpan.Zero, null),
        };
    }

    /// <summary>
    /// Runs posted actions immediately.
    /// </summary>
    private sealed class InlineDispatcher : IUiDispatcher
    {
        /// <inheritdoc />
        public void Post(Action action) => action();
    }
}
