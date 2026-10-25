using System.Buffers.Binary;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Golether.Components.Installation;
using Golether.Core.Data.Enums;
using Golether.Core.Data.Stores;
using Golether.Core.Identity;
using Golether.Media.Conference;
using Golether.Session;
using Golether.UI.Enums;
using Golether.UI.Services;
using Golether.UI.ViewModels;
using Golether.UI.ViewModels.Dialogs;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of the notice of new chat messages: the clapperboard sound, its setting and the marks on the side panel.
/// </summary>
public sealed class ChatSoundTests
{
    /// <summary>
    /// A participant.
    /// </summary>
    private static readonly PeerId _guest = PeerId.Parse(new string('b', 64));

    /// <summary>
    /// The session.
    /// </summary>
    private readonly ISessionService _session = Substitute.For<ISessionService>();

    /// <summary>
    /// The settings.
    /// </summary>
    private readonly ISettingsStore _settings = Substitute.For<ISettingsStore>();

    /// <summary>
    /// The player whose calls are checked.
    /// </summary>
    private readonly ISoundPlayer _player = Substitute.For<ISoundPlayer>();

    /// <summary>
    /// The test clock.
    /// </summary>
    private readonly FakeTimeProvider _time = new();

    /// <summary>
    /// Only text messages of the others clap: not this device's own message, which the host sends back, and not
    /// reactions.
    /// </summary>
    [Fact]
    public void Sound_IsPlayedOnlyForTextOfOthers()
    {
        _ = CreateNotifier();

        Receive(ChatKind.Text, local: true);
        Receive(ChatKind.Reaction, local: false);
        _player.DidNotReceiveWithAnyArgs().Play(default!);

        Receive(ChatKind.Text, local: false);
        _player.Received(1).Play(Arg.Is<byte[]>(wave => wave.Length > 44));
    }

    /// <summary>
    /// A burst of messages claps once; the next message after the pause claps again.
    /// </summary>
    [Fact]
    public void Burst_ClapsOnce()
    {
        _ = CreateNotifier();

        Receive(ChatKind.Text, local: false);
        _time.Advance(TimeSpan.FromMilliseconds(400));
        Receive(ChatKind.Text, local: false);
        _time.Advance(TimeSpan.FromMilliseconds(400));
        Receive(ChatKind.Text, local: false);
        _player.ReceivedWithAnyArgs(1).Play(default!);

        _time.Advance(ChatSoundNotifier.MinimumInterval);
        Receive(ChatKind.Text, local: false);
        _player.ReceivedWithAnyArgs(2).Play(default!);
    }

    /// <summary>
    /// The sound is on by default; switched off it is silent and the choice is stored and read back.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Setting_SwitchesTheSound()
    {
        var notifier = CreateNotifier();
        await notifier.LoadAsync();
        Assert.True(notifier.Enabled);

        notifier.SetEnabled(false);
        Receive(ChatKind.Text, local: false);
        _player.DidNotReceiveWithAnyArgs().Play(default!);
        await _settings.Received(1).SetAsync(ChatSoundNotifier.SETTING, "false", Arg.Any<CancellationToken>());

        _settings.GetAsync(ChatSoundNotifier.SETTING, Arg.Any<CancellationToken>()).Returns("false");
        var again = CreateNotifier();
        await again.LoadAsync();
        Assert.False(again.Enabled);
    }

    /// <summary>
    /// The built-in sound is a 16-bit mono PCM WAV of 48 kHz and a quarter of a second, starting at once, ending in
    /// silence and neither too quiet nor too loud.
    /// </summary>
    [Fact]
    public void Clapperboard_IsAShortModerateWave()
    {
        const int SAMPLE_RATE = 48000;
        const int SAMPLES = SAMPLE_RATE / 4;
        var wave = ClapperboardSound.Load();

        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wave, 8, 4));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(20)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(22)));
        Assert.Equal(SAMPLE_RATE, BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(24)));
        Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(34)));
        Assert.Equal(SAMPLES * 2, BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(40)));
        Assert.Equal(44 + SAMPLES * 2, wave.Length);

        var samples = Enumerable.Range(0, SAMPLES).Select(i => BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(44 + i * 2))).ToArray();
        var peak = samples.Max(s => Math.Abs((int)s)) / (double)short.MaxValue;
        Assert.InRange(peak, 0.25, 0.4);
        Assert.Contains(samples.Take(SAMPLE_RATE / 100), s => Math.Abs((int)s) > 1000);
        Assert.Equal(0, samples[^1]);
    }

    /// <summary>
    /// The settings window shows the current choice and hands a change over only when the settings are applied.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task SettingsWindow_AppliesTheSoundOnlyOnApply()
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
            canChooseSounds: true,
            chatSound: true,
            applyChatSound: applied.Add);
        Assert.True(settings.CanChooseSounds);
        Assert.True(settings.ChatSound);

        settings.ChatSound = false;
        Assert.Empty(applied);

        await settings.ApplyAsync();
        Assert.Equal([false], applied);
    }

    /// <summary>
    /// The main window offers the switch with the stored choice and stores what the settings window applied.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task MainWindow_LoadsAndAppliesTheChoice()
    {
        _settings.GetAsync(ChatSoundNotifier.SETTING, Arg.Any<CancellationToken>()).Returns("false");
        var notifier = CreateNotifier();
        var dialogs = Substitute.For<IDialogService>();
        SettingsViewModel? shown = null;
        dialogs.ShowSettingsAsync(Arg.Do<SettingsViewModel>(model => shown = model)).Returns(Task.CompletedTask);
        var viewModel = CreateViewModel(dialogs, notifier);
        await viewModel.LoadAsync();
        Assert.False(notifier.Enabled);

        await viewModel.OpenSettingsCommand.ExecuteAsync(null);
        Assert.NotNull(shown);
        Assert.True(shown.CanChooseSounds);
        Assert.False(shown.ChatSound);

        shown.ChatSound = true;
        await shown.ApplyAsync();
        Assert.True(notifier.Enabled);
        await _settings.Received(1).SetAsync(ChatSoundNotifier.SETTING, "true", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The button of the side panel draws attention only while the panel is hidden and unread messages wait in the
    /// chat; the event feed never does.
    /// </summary>
    [Fact]
    public void PanelToggle_IsHighlightedForUnreadChatWhileHidden()
    {
        var viewModel = CreateViewModel(Substitute.For<IDialogService>(), chatSound: null);
        var changes = 0;
        viewModel.PropertyChanged += (_, e) => changes += e.PropertyName == nameof(MainWindowViewModel.HighlightPanelToggle) ? 1 : 0;

        // The events tab is open: a message makes the chat tab unread, but the open panel needs no stripe.
        viewModel.SideTab = SideTab.Events;
        Receive(ChatKind.Text, local: false);
        Assert.True(viewModel.Chat.HasUnread);
        Assert.False(viewModel.HighlightPanelToggle);

        viewModel.ToggleEventsCommand.Execute(null);
        Assert.True(viewModel.HighlightPanelToggle);

        // Opening the chat reads the messages and stops the stripe.
        viewModel.ShowTabCommand.Execute(SideTab.Chat);
        Assert.False(viewModel.Chat.HasUnread);
        Assert.False(viewModel.HighlightPanelToggle);
        Assert.True(changes >= 2);

        // A hidden panel with nothing new stays plain.
        viewModel.ToggleEventsCommand.Execute(null);
        Assert.False(viewModel.HighlightPanelToggle);
        Receive(ChatKind.Reaction, local: false);
        Receive(ChatKind.Text, local: true);
        Assert.False(viewModel.HighlightPanelToggle);
    }

    /// <summary>
    /// Creates the notifier over the substitutes.
    /// </summary>
    /// <returns>The notifier.</returns>
    private ChatSoundNotifier CreateNotifier() => new(_session, _settings, _player, _time);

    /// <summary>
    /// Builds the main view model over substitutes.
    /// </summary>
    /// <param name="dialogs">The dialogs.</param>
    /// <param name="chatSound">The sound of new messages, or <see langword="null"/>.</param>
    /// <returns>The view model.</returns>
    private MainWindowViewModel CreateViewModel(IDialogService dialogs, ChatSoundNotifier? chatSound)
    {
        var components = Substitute.For<IComponentService>();
        foreach (var id in new[] { ComponentId.Video, ComponentId.Conference })
        {
            components.GetStatus(id).Returns(new ComponentStatus(id, ComponentSource.Bundled, "x", null, null));
        }

        return new MainWindowViewModel(
            _session,
            dialogs,
            _settings,
            new InlineDispatcher(),
            _ => throw new NotSupportedException(),
            "A249-B9CC",
            Substitute.For<IConferenceMedia>(),
            components,
            timeProvider: _time,
            chatSound: chatSound);
    }

    /// <summary>
    /// Simulates a message relayed by the host.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="local">Whether this device sent it.</param>
    private void Receive(ChatKind kind, bool local)
        => _session.ChatReceived += Raise.Event<EventHandler<ChatEntry>>(
            _session,
            new ChatEntry("0A", _guest, "Марина", kind, kind == ChatKind.Reaction ? "👍" : "привет", _time.GetLocalNow(), local));

    /// <summary>
    /// Runs posted actions immediately.
    /// </summary>
    private sealed class InlineDispatcher : IUiDispatcher
    {
        /// <inheritdoc />
        public void Post(Action action) => action();
    }
}
