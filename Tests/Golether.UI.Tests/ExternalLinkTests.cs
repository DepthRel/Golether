using Golether.Components.Installation;
using Golether.Core.Data.Enums;
using Golether.Core.Data.Stores;
using Golether.Core.Identity;
using Golether.Core.Networking;
using Golether.Media.Conference;
using Golether.Security.Invites;
using Golether.UI.Services;
using Golether.UI.ViewModels;
using NSubstitute;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of what reaches the main window from outside and of the choices added with it: invitations opened by a
/// link, the way a hosted session finds its rendezvous server, and the offer of a new version.
/// </summary>
public sealed class ExternalLinkTests
{
    /// <summary>
    /// The session service.
    /// </summary>
    private readonly ISessionService _session = Substitute.For<ISessionService>();

    /// <summary>
    /// The dialogs.
    /// </summary>
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    /// <summary>
    /// The settings of the device.
    /// </summary>
    private readonly MemorySettings _settings = new();

    /// <summary>
    /// The update source.
    /// </summary>
    private readonly IUpdateService _updates = Substitute.For<IUpdateService>();

    /// <summary>
    /// A link clicked in the browser is not joined silently: the user sees who invites and agrees first.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Link_AsksBeforeJoining()
    {
        var link = Sample().ToWebLink();
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        var viewModel = CreateViewModel();

        await viewModel.OpenExternalAsync([link]);

        await _dialogs.Received(1).ConfirmAsync(Arg.Any<string>(), Arg.Is<string>(text => text.Contains("Вечер кино") && text.Contains("Марина")), Arg.Any<string>());
        _dialogs.Received(1).BringToFront();
        await _session.Received(1).JoinAsync(link, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _session.DidNotReceive().LeaveAsync();
    }

    /// <summary>
    /// A declined invitation changes nothing.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Link_DeclinedJoinsNothing()
    {
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        var viewModel = CreateViewModel();

        await viewModel.OpenExternalAsync([Sample().ToLink()]);

        await _session.DidNotReceive().JoinAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Empty(viewModel.InviteLink);
    }

    /// <summary>
    /// In a session the user is asked whether to leave it; only after a yes the current session is left and the
    /// new one joined, in this order.
    /// </summary>
    /// <param name="agreed">Whether the user agrees to switch.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Link_OffersToSwitchSessions(bool agreed)
    {
        var link = Sample().ToLink();
        _session.IsActive.Returns(true);
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(agreed);
        var viewModel = CreateViewModel();

        await viewModel.OpenExternalAsync([link]);

        await _dialogs.Received(1).ConfirmAsync(Arg.Any<string>(), Arg.Is<string>(text => text.Contains("Вечер кино")), Arg.Any<string>());
        if (agreed)
        {
            Received.InOrder(() =>
            {
                _session.LeaveAsync();
                _session.JoinAsync(link, Arg.Any<string>(), Arg.Any<CancellationToken>());
            });
        }
        else
        {
            await _session.DidNotReceive().LeaveAsync();
            await _session.DidNotReceive().JoinAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
    }

    /// <summary>
    /// A damaged invitation is reported and never offered.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Link_DamagedIsReported()
    {
        var viewModel = CreateViewModel();

        await viewModel.OpenExternalAsync([Invite.WebLinkPrefix + "damaged"]);

        await _dialogs.Received(1).ShowErrorAsync(Arg.Any<string>(), Arg.Any<string>());
        await _dialogs.DidNotReceive().ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        await _session.DidNotReceive().JoinAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Arguments that are neither an invitation nor an existing file do nothing.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task OtherArguments_AreIgnored()
    {
        var viewModel = CreateViewModel();

        await viewModel.OpenExternalAsync(["--unknown", "https://example.org/join/#abc", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mkv")]);
        await viewModel.OpenExternalAsync([]);

        Assert.Empty(_dialogs.ReceivedCalls());
        await _session.DidNotReceive().StartHostingAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A new device finds the rendezvous server by itself; a device that had an address typed keeps using it; a
    /// stored choice wins over both.
    /// </summary>
    /// <param name="storedMode">The stored choice, or <see langword="null"/>.</param>
    /// <param name="storedServer">The stored address, or <see langword="null"/>.</param>
    /// <param name="expected">The way hosted sessions find the server.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData(null, null, RendezvousMode.Automatic)]
    [InlineData(null, "203.0.113.7:47900", RendezvousMode.Manual)]
    [InlineData("Direct", "203.0.113.7:47900", RendezvousMode.Direct)]
    [InlineData("Automatic", "203.0.113.7:47900", RendezvousMode.Automatic)]
    [InlineData("Manual", null, RendezvousMode.Manual)]
    [InlineData("nonsense", null, RendezvousMode.Automatic)]
    [InlineData("7", null, RendezvousMode.Automatic)]
    public async Task Load_ChoosesHowTheRendezvousServerIsFound(string? storedMode, string? storedServer, RendezvousMode expected)
    {
        if (storedMode is not null)
        {
            _settings.Values[MainWindowViewModel.RendezvousModeSetting] = storedMode;
        }

        if (storedServer is not null)
        {
            _settings.Values[MainWindowViewModel.RendezvousServerSetting] = storedServer;
        }

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        Assert.Equal(expected, viewModel.RendezvousMode);
        Assert.Equal(expected, _session.RendezvousMode);
        Assert.Equal(expected == RendezvousMode.Automatic, viewModel.IsRendezvousAutomatic);
        Assert.Equal(expected == RendezvousMode.Direct, viewModel.IsRendezvousDirect);
        Assert.Equal(expected == RendezvousMode.Manual, viewModel.IsRendezvousManual);
    }

    /// <summary>
    /// The three switches choose the mode, pass it to the sessions and store it; releasing a switch chooses nothing.
    /// </summary>
    [Fact]
    public void Switches_ChooseTheMode()
    {
        var viewModel = CreateViewModel();

        viewModel.IsRendezvousManual = true;
        viewModel.IsRendezvousAutomatic = false;

        Assert.Equal(RendezvousMode.Manual, viewModel.RendezvousMode);
        Assert.Equal(RendezvousMode.Manual, _session.RendezvousMode);
        Assert.Equal("Manual", _settings.Values[MainWindowViewModel.RendezvousModeSetting]);

        viewModel.IsRendezvousDirect = true;
        Assert.Equal("Direct", _settings.Values[MainWindowViewModel.RendezvousModeSetting]);
        Assert.False(viewModel.IsRendezvousManual);
    }

    /// <summary>
    /// A new version is offered with a way to its download page; closing the offer with "ignore this version"
    /// keeps that version, and only that one, from being offered again.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Update_IsOfferedAndCanBeIgnored()
    {
        var release = new UpdateInfo("9.9.9", "Новое", "https://example.org/golether-9.9.9.exe", null, 0, "https://example.org/download");
        _updates.CheckAsync(Arg.Any<CancellationToken>()).Returns(release);
        var viewModel = CreateViewModel();

        await viewModel.CheckUpdatesAsync();
        Assert.True(viewModel.HasUpdate);
        Assert.Contains("9.9.9", viewModel.UpdateText, StringComparison.Ordinal);

        await viewModel.OpenUpdatePageCommand.ExecuteAsync(null);
        await _dialogs.Received(1).OpenLinkAsync("https://example.org/download");
        Assert.True(viewModel.HasUpdate);

        // Closing the offer without the tick hides it for now only.
        viewModel.DismissUpdateCommand.Execute(null);
        Assert.False(viewModel.HasUpdate);
        Assert.DoesNotContain(UpdateService.IgnoredVersionSetting, _settings.Values.Keys);
        await viewModel.CheckUpdatesAsync();
        Assert.True(viewModel.HasUpdate);

        viewModel.IgnoreUpdate = true;
        viewModel.DismissUpdateCommand.Execute(null);
        Assert.Equal("9.9.9", _settings.Values[UpdateService.IgnoredVersionSetting]);
        await viewModel.CheckUpdatesAsync();
        Assert.False(viewModel.HasUpdate);

        // The next version is offered again, with the tick cleared.
        _updates.CheckAsync(Arg.Any<CancellationToken>()).Returns(release with { Version = "10.0.0" });
        await viewModel.CheckUpdatesAsync();
        Assert.True(viewModel.HasUpdate);
        Assert.False(viewModel.IgnoreUpdate);
    }

    /// <summary>
    /// The settings window stores whether to look for new versions and where.
    /// </summary>
    [Fact]
    public void UpdateSource_IsStored()
    {
        var viewModel = CreateViewModel();

        viewModel.SetUpdateSource(false, "  https://example.org/updates.json ");

        Assert.False(_updates.Enabled);
        Assert.Equal("https://example.org/updates.json", _updates.ManifestUrl);
        Assert.Equal("false", _settings.Values[UpdateService.EnabledSetting]);
        Assert.Equal("https://example.org/updates.json", _settings.Values[UpdateService.UrlSetting]);
    }

    /// <summary>
    /// Creates an invitation the host named and addressed.
    /// </summary>
    /// <returns>The invitation.</returns>
    private static Invite Sample() => new()
    {
        HostPeerId = PeerId.Parse(new string('a', 64)),
        HostName = "Марина",
        SessionName = "Вечер кино",
        Endpoints = [PeerEndpoint.Parse("192.168.1.2:47800")],
        Token = Invite.CreateToken(),
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
    };

    /// <summary>
    /// Creates the view model.
    /// </summary>
    /// <returns>The view model.</returns>
    private MainWindowViewModel CreateViewModel()
    {
        var components = Substitute.For<IComponentService>();
        foreach (var id in new[] { ComponentId.Video, ComponentId.Conference })
        {
            components.GetStatus(id).Returns(new ComponentStatus(id, ComponentSource.Bundled, "x", null, null));
        }

        return new MainWindowViewModel(
            _session,
            _dialogs,
            _settings,
            new InlineDispatcher(),
            _ => throw new NotSupportedException(),
            "A249-B9CC",
            Substitute.For<IConferenceMedia>(),
            components,
            updates: _updates);
    }

    /// <summary>
    /// Runs posted actions at once.
    /// </summary>
    private sealed class InlineDispatcher : IUiDispatcher
    {
        /// <inheritdoc />
        public void Post(Action action) => action();
    }

    /// <summary>
    /// Settings kept in memory.
    /// </summary>
    private sealed class MemorySettings : ISettingsStore
    {
        /// <summary>
        /// Gets the stored values.
        /// </summary>
        public Dictionary<string, string> Values { get; } = [];

        /// <inheritdoc />
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
            => Task.FromResult(Values.GetValueOrDefault(key));

        /// <inheritdoc />
        public Task SetAsync(string key, string value, CancellationToken cancellationToken)
        {
            Values[key] = value;
            return Task.CompletedTask;
        }
    }
}
