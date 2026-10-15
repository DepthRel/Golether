using NSubstitute;
using Golether.Components.Installation;
using Golether.Core.Data.Enums;
using Golether.Core.Data.Stores;
using Golether.Core.Identity;
using Golether.Core.Playback;
using Golether.Core.Session;
using Golether.Media.Conference;
using Golether.Session;
using Golether.UI.Services;
using Golether.UI.ViewModels;
using Golether.UI.ViewModels.Dialogs;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of the adjustable layout: the border between the video and the participants.
/// </summary>
public sealed class LayoutSettingsTests
{
    /// <summary>
    /// The settings substitute.
    /// </summary>
    private readonly ISettingsStore _settings = Substitute.For<ISettingsStore>();

    /// <summary>
    /// Builds the main view model over substitutes.
    /// </summary>
    /// <param name="session">The session service, or <see langword="null"/> for one without a session.</param>
    /// <returns>The view model.</returns>
    private MainWindowViewModel CreateViewModel(ISessionService? session = null)
    {
        var components = Substitute.For<IComponentService>();
        foreach (var id in new[] { ComponentId.Video, ComponentId.Conference })
        {
            components.GetStatus(id).Returns(new ComponentStatus(id, ComponentSource.Bundled, "x", null, null));
        }

        return new MainWindowViewModel(
            session ?? Substitute.For<ISessionService>(),
            Substitute.For<IDialogService>(),
            _settings,
            new InlineDispatcher(),
            _ => throw new NotSupportedException(),
            "A249-B9CC",
            Substitute.For<IConferenceMedia>(),
            components);
    }

    /// <summary>
    /// The panel is never narrower than designed and never leaves the video less than its minimum.
    /// </summary>
    /// <param name="wanted">The width asked for.</param>
    /// <param name="available">The width shared by the video and the panel.</param>
    /// <param name="expected">The width to use.</param>
    [Theory]
    [InlineData(300, 1280, 300)]
    [InlineData(520, 1280, 520)]
    [InlineData(100, 1280, 300)]
    [InlineData(2000, 1280, 800)]
    [InlineData(520, 700, 300)]
    [InlineData(double.NaN, 1280, 300)]
    public void Width_IsFittedIntoTheWindow(double wanted, double available, double expected)
        => Assert.Equal(expected, MainWindowViewModel.ClampSidePanelWidth(wanted, available));

    /// <summary>
    /// Without stored settings the layout is the designed one and the border cannot be dragged.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Default_IsTheDesignedLayout()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        Assert.False(viewModel.IsLayoutResizable);
        Assert.Equal(MainWindowViewModel.DEFAULT_SIDE_PANEL_WIDTH, viewModel.SidePanelWidth);
    }

    /// <summary>
    /// A stored width comes back with the adjustable layout, and is ignored while the layout is locked.
    /// </summary>
    /// <param name="resizable">The stored switch.</param>
    /// <param name="expected">The width after loading.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData("true", 460)]
    [InlineData("false", 300)]
    public async Task StoredWidth_IsUsedOnlyWithTheAdjustableLayout(string resizable, double expected)
    {
        _settings.GetAsync(MainWindowViewModel.LAYOUT_RESIZABLE_SETTING, Arg.Any<CancellationToken>()).Returns(resizable);
        _settings.GetAsync(MainWindowViewModel.SIDE_PANEL_WIDTH_SETTING, Arg.Any<CancellationToken>()).Returns("460");
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync();

        Assert.Equal(resizable == "true", viewModel.IsLayoutResizable);
        Assert.Equal(expected, viewModel.SidePanelWidth);
    }

    /// <summary>
    /// A dragged width is remembered; the reset (a double click on the border) and switching the adjustable layout
    /// off both come back to the designed width and remember that.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task DraggedWidth_IsStored_AndResetOrSwitchingOffRestoresTheDesignedOne()
    {
        var viewModel = CreateViewModel();
        viewModel.SetLayoutResizable(true);
        await _settings.Received(1).SetAsync(MainWindowViewModel.LAYOUT_RESIZABLE_SETTING, "true", Arg.Any<CancellationToken>());

        viewModel.StoreSidePanelWidth(452.4);
        Assert.Equal(452, viewModel.SidePanelWidth);
        await _settings.Received(1).SetAsync(MainWindowViewModel.SIDE_PANEL_WIDTH_SETTING, "452", Arg.Any<CancellationToken>());

        viewModel.ResetSidePanelWidth();
        Assert.Equal(300, viewModel.SidePanelWidth);

        viewModel.StoreSidePanelWidth(500);
        viewModel.SetLayoutResizable(false);
        Assert.False(viewModel.IsLayoutResizable);
        Assert.Equal(300, viewModel.SidePanelWidth);
        await _settings.Received(1).SetAsync(MainWindowViewModel.LAYOUT_RESIZABLE_SETTING, "false", Arg.Any<CancellationToken>());
        await _settings.Received(2).SetAsync(MainWindowViewModel.SIDE_PANEL_WIDTH_SETTING, "300", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The tiles go in two columns only when there are more than three participants and the panel is more than
    /// three times as wide as designed.
    /// </summary>
    /// <param name="participants">The number of participants.</param>
    /// <param name="width">The width of the panel.</param>
    /// <param name="expected">The number of columns.</param>
    [Theory]
    [InlineData(2, 1400, 1)]
    [InlineData(3, 1400, 1)]
    [InlineData(4, 900, 1)]
    [InlineData(4, 901, 2)]
    [InlineData(6, 1400, 2)]
    [InlineData(6, 300, 1)]
    public void Tiles_GoInTwoColumnsOnlyWhenManyAndWide(int participants, double width, int expected)
        => Assert.Equal(expected, MainWindowViewModel.TileColumnsFor(participants, width));

    /// <summary>
    /// Tiles swap places with the adjustable layout only; a participant who joins afterwards goes to the end, and
    /// switching the layout off restores the order of the session.
    /// </summary>
    [Fact]
    public void Tiles_SwapAndReturnToTheSessionOrder()
    {
        var session = Substitute.For<ISessionService>();
        var peers = "abcd".Select(c => PeerId.Parse(new string(c, 64))).ToArray();
        var present = 3;
        session.GetSnapshot().Returns(_ => new SessionSnapshot
        {
            IsHost = true,
            State = SessionState.Active,
            SessionName = "Вечер кино",
            HostPeerId = peers[0],
            Participants = [.. peers.Take(present).Select((p, i) => new ParticipantView(new ParticipantInfo(p, $"P{i}", i == 0), null, i == 0))],
            Playback = PlaybackState.Initial(peers[0], 0),
        });
        var viewModel = CreateViewModel(session);
        viewModel.Refresh();
        string Order() => string.Concat(viewModel.Participants.Select(p => p.Name[1]));
        Assert.Equal("012", Order());

        // A locked layout keeps the order.
        viewModel.SwapParticipants(viewModel.Participants[0], viewModel.Participants[2]);
        Assert.Equal("012", Order());

        viewModel.SetLayoutResizable(true);
        viewModel.SwapParticipants(viewModel.Participants[0], viewModel.Participants[2]);
        Assert.Equal("210", Order());
        viewModel.SwapParticipants(viewModel.Participants[2], viewModel.Participants[1]);
        Assert.Equal("201", Order());

        // The refresh keeps the chosen order and puts a newcomer at the end.
        present = 4;
        viewModel.Refresh();
        Assert.Equal("2013", Order());

        viewModel.SetLayoutResizable(false);
        Assert.Equal("0123", Order());
    }

    /// <summary>
    /// "Always show the timeline" is off by default, comes back from the settings and is remembered when changed.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task AlwaysShowBars_IsOffByDefaultAndRemembered()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();
        Assert.False(viewModel.AlwaysShowBars);

        viewModel.SetAlwaysShowBars(true);
        Assert.True(viewModel.AlwaysShowBars);
        await _settings.Received(1).SetAsync(MainWindowViewModel.ALWAYS_SHOW_BARS_SETTING, "true", Arg.Any<CancellationToken>());

        _settings.GetAsync(MainWindowViewModel.ALWAYS_SHOW_BARS_SETTING, Arg.Any<CancellationToken>()).Returns("true");
        var again = CreateViewModel();
        await again.LoadAsync();
        Assert.True(again.AlwaysShowBars);
    }

    /// <summary>
    /// The settings window shows the current switch and hands a change over only when the settings are applied.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task SettingsWindow_AppliesTheSwitchOnlyOnApply()
    {
        var applied = new List<bool>();
        var settings = new SettingsViewModel(
            language: null,
            devices: null,
            conferenceAvailable: true,
            conferenceNotice: string.Empty,
            canChooseAvatar: false,
            canConfigureTunnels: true,
            avatarPreview: null,
            pickAvatar: () => Task.FromResult<AvatarSelection?>(null),
            applyAvatar: _ => Task.CompletedTask,
            openTunnels: () => Task.CompletedTask,
            layoutResizable: false,
            applyLayoutResizable: applied.Add,
            alwaysShowBars: true,
            applyAlwaysShowBars: applied.Add);
        Assert.False(settings.LayoutResizable);
        Assert.True(settings.AlwaysShowBars);

        settings.LayoutResizable = true;
        settings.AlwaysShowBars = false;
        Assert.Empty(applied);

        await settings.ApplyAsync();
        Assert.Equal([true, false], applied);
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
