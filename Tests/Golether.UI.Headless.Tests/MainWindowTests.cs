using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Golether.Components.Catalog;
using Golether.Components.Installation;
using Golether.Core.Data.Stores;
using Golether.Core.Identity;
using Golether.Core.Media;
using Golether.Core.Playback;
using Golether.Core.Session;
using Golether.Media.Conference;
using Golether.Media.Player;
using Golether.Session;
using Golether.Sync.Protocol;
using Golether.UI;
using Golether.UI.Controls;
using Golether.UI.Services;
using Golether.UI.ViewModels;
using Golether.UI.Views;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Golether.UI.Headless.Tests;

/// <summary>
/// Builds the application for the headless platform.
/// </summary>
public static class TestApp
{
    /// <summary>
    /// Configures Avalonia without a screen.
    /// </summary>
    /// <returns>The application builder.</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// Tests of the main window: the participant menu, the event feed, the volume pop-up and clicks on the video.
/// </summary>
public sealed class MainWindowTests
{
    /// <summary>
    /// The headless session shared by the tests (Avalonia starts once per process).
    /// </summary>
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(() => HeadlessUnitTestSession.StartNew(typeof(TestApp)));

    /// <summary>
    /// This device, the host.
    /// </summary>
    private static readonly PeerId Host = PeerId.Parse(new string('a', 64));

    /// <summary>
    /// A participant.
    /// </summary>
    private static readonly PeerId Guest = PeerId.Parse(new string('b', 64));

    /// <summary>
    /// A right click on a participant tile opens the menu: the local voice volume, and for the host the two
    /// switch-off commands; the own tile has no menu; the commands reach the session and the conference.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task ParticipantMenu_OnlyForOtherParticipants() => RunAsync(async fixture =>
    {
        var guestTile = fixture.Tile("Марина");
        var hostTile = fixture.Tile("Вы");

        fixture.Click(hostTile, MouseButton.Right);
        Assert.False(hostTile.ContextMenu!.IsOpen, "The own tile has no menu.");

        fixture.Click(guestTile, MouseButton.Right);
        var menu = guestTile.ContextMenu!;
        Assert.True(menu.IsOpen);
        await fixture.Settle();
        var items = menu.Items.OfType<MenuItem>().ToArray();
        var texts = items.Select(i => i.Header as string).ToArray();
        Assert.Equal([null, "Заглушить у себя", "Обычная громкость (100 %)", "Выключить микрофон", "Выключить камеру"], texts);
        Assert.All(items, i => Assert.True(i.IsVisible));
        Assert.DoesNotContain(texts, t => t?.Contains("Включить", StringComparison.Ordinal) == true);

        items[3].Command!.Execute(null);
        await fixture.Settle();
        await fixture.Sessions.Received(1).SwitchOffParticipantDevicesAsync(Guest, true, false);

        // The voice volume changes only here and is remembered for this participant.
        var slider = menu.GetLogicalDescendants().OfType<Slider>().Single();
        Assert.Equal((0d, 200d, 70d), (slider.Minimum, slider.Maximum, slider.Value));
        slider.Value = 50;
        await fixture.Settle();
        fixture.Conference.Received().SetVoiceVolume(Guest, 0.5);
        items[1].Command!.Execute(null);
        fixture.Conference.Received().SetVoiceVolume(Guest, 0);
        Assert.Equal("Вернуть звук", items[1].Header);
        await Task.Delay(MainWindowViewModel.VoiceVolumeSaveDelay + TimeSpan.FromMilliseconds(300));
        await fixture.Settings.Received(1).SetAsync(MainWindowViewModel.VoiceVolumeSettingPrefix + Guest.Value, "0", Arg.Any<CancellationToken>());
        menu.Close();

        // A microphone that is already off cannot be switched off again.
        fixture.GuestMicrophoneOff = true;
        fixture.ViewModel.Refresh();
        await fixture.Settle();
        Assert.False(items[3].Command!.CanExecute(null));

        // A participant (not the host) gets only the volume.
        fixture.IsHost = false;
        fixture.ViewModel.Refresh();
        await fixture.Settle();
        fixture.Click(guestTile, MouseButton.Right);
        await fixture.Settle();
        Assert.True(menu.IsOpen);
        Assert.Equal([true, true, true, false, false], items.Select(i => i.IsVisible));
        menu.Close();
    });

    /// <summary>
    /// A stored voice volume is applied when the participant appears.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task StoredVoiceVolume_IsApplied() => RunAsync(async fixture =>
    {
        await fixture.Settle();
        var tile = fixture.ViewModel.Participants.Single(p => p.PeerId == Guest);
        Assert.Equal(70, tile.VoiceVolume);
        fixture.Conference.Received().SetVoiceVolume(Guest, 0.7);
        fixture.Conference.DidNotReceive().SetVoiceVolume(Host, Arg.Any<double>());
    });

    /// <summary>
    /// The events tab shows the feed; a click on the feed hides it; the tab shows it again; the chat tab switches.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task EventFeed_FoldsAndUnfolds() => RunAsync(async fixture =>
    {
        var feed = fixture.Window.FindControl<ScrollViewer>("EventFeed")!;
        var chat = fixture.Window.FindControl<TextBox>("ChatInput")!;
        var tab = fixture.Window.GetVisualDescendants().OfType<Button>().Single(b => AutomationName(b) == "Лента событий");
        Assert.False(feed.IsVisible);
        Assert.True(chat.IsEffectivelyVisible);

        fixture.Click(tab, MouseButton.Left);
        await fixture.Settle();
        Assert.True(feed.IsVisible);
        Assert.False(chat.IsEffectivelyVisible);

        fixture.Click(feed, MouseButton.Left);
        await fixture.Settle();
        Assert.False(feed.IsVisible);
        Assert.False(fixture.ViewModel.EventsExpanded);

        fixture.Click(tab, MouseButton.Left);
        await fixture.Settle();
        Assert.True(feed.IsVisible);

        fixture.Click(fixture.Window.GetVisualDescendants().OfType<Button>().Single(b => AutomationName(b) == "Чат"), MouseButton.Left);
        await fixture.Settle();
        Assert.False(feed.IsVisible);
        Assert.True(chat.IsEffectivelyVisible);
    });

    /// <summary>
    /// Hovering the speaker opens the vertical scale, leaving closes it, the wheel changes the level, and the click
    /// mutes only this device.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Volume_PopupWheelAndMute() => RunAsync(async fixture =>
    {
        var speaker = fixture.Window.FindControl<Button>("VolumeButton")!;
        var popup = fixture.Window.FindControl<Popup>("VolumePopup")!;
        Assert.False(popup.IsOpen);

        fixture.Move(speaker);
        await fixture.Settle();
        Assert.True(popup.IsOpen);
        var slider = fixture.Window.FindControl<Slider>("VolumeSlider")!;
        Assert.Equal(Avalonia.Layout.Orientation.Vertical, slider.Orientation);
        Assert.Equal((0d, 100d), (slider.Minimum, slider.Maximum));

        fixture.Window.MouseWheel(fixture.PointOf(speaker), new Vector(0, -1), RawInputModifiers.None);
        await fixture.Settle();
        Assert.Equal(95, fixture.ViewModel.Volume!.Volume);

        slider.Value = 0;
        await fixture.Settle();
        Assert.True(fixture.ViewModel.Volume.IsMuted);
        fixture.Player.Received().SetMuted(true);

        fixture.Click(speaker, MouseButton.Left);
        await fixture.Settle();
        Assert.False(fixture.ViewModel.Volume.IsMuted);
        Assert.Equal(95, fixture.ViewModel.Volume.Volume);
        await fixture.Sessions.DidNotReceiveWithAnyArgs().RequestAsync(default!, default);

        fixture.Window.MouseMove(new Point(2, 2));
        await Task.Delay(600);
        await fixture.Settle();
        Assert.False(popup.IsOpen);
    });

    /// <summary>
    /// A click on the video toggles playback after a short wait; a double click switches to full screen without
    /// toggling playback.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task VideoClicks_TogglePlaybackAndFullScreen() => RunAsync(async fixture =>
    {
        fixture.VideoPointer(VideoPointerAction.Click);
        await Task.Delay(100);
        await fixture.Settle();
        await fixture.Sessions.DidNotReceiveWithAnyArgs().RequestAsync(default!, default);
        await Task.Delay(500);
        await fixture.Settle();
        await fixture.Sessions.Received(1).RequestAsync(Arg.Is<PlaybackRequest>(r => r.Kind == PlaybackRequestKind.Play), Arg.Any<CancellationToken>());

        fixture.Sessions.ClearReceivedCalls();
        fixture.VideoPointer(VideoPointerAction.Click);
        fixture.VideoPointer(VideoPointerAction.DoubleClick);
        fixture.VideoPointer(VideoPointerAction.Click);
        await Task.Delay(600);
        await fixture.Settle();
        Assert.Equal(WindowState.FullScreen, fixture.Window.WindowState);
        await fixture.Sessions.DidNotReceiveWithAnyArgs().RequestAsync(default!, default);

        fixture.VideoPointer(VideoPointerAction.DoubleClick);
        await fixture.Settle();
        Assert.NotEqual(WindowState.FullScreen, fixture.Window.WindowState);
    });

    /// <summary>
    /// In a narrow window the actions fold into the "more" menu and nothing leaves the window; a wide window shows
    /// them again.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task TopBar_FoldsActionsWhenNarrow() => RunAsync(async fixture =>
    {
        var panel = fixture.Window.FindControl<OverflowPanel>("TopActions")!;
        var more = fixture.Window.FindControl<Button>("MoreActions")!;
        var leave = panel.Children.OfType<Button>().Single(b => (string?)b.Content == "Завершить");

        fixture.Window.Width = 1600;
        await fixture.Settle();
        Assert.False(panel.IsOverflowing);
        Assert.DoesNotContain(OverflowPanel.OverflowedClass, leave.Classes);
        Assert.Contains(OverflowPanel.OverflowedClass, more.Classes);
        Assert.True(IsInside(leave, fixture.Window));

        fixture.Window.Width = 960;
        await fixture.Settle();
        Assert.True(panel.IsOverflowing);
        Assert.Contains(OverflowPanel.OverflowedClass, leave.Classes);
        Assert.Equal(0, leave.Bounds.Width);
        Assert.DoesNotContain(OverflowPanel.OverflowedClass, more.Classes);
        Assert.True(IsInside(more, fixture.Window), "The more button stays inside the window.");
        foreach (var child in panel.Children.Where(c => c.Bounds.Width > 0))
        {
            Assert.True(IsInside(child, fixture.Window), $"{child} leaves the window.");
        }

        more.Flyout!.ShowAt(more);
        await fixture.Settle();
        var items = ((MenuFlyout)more.Flyout).Items.OfType<MenuItem>().ToArray();
        Assert.Equal(["Туннели AWG", "Пригласить", "Отчёт для диагностики…", "Завершить"], items.Select(i => (string)i.Header!));
        Assert.Same(fixture.ViewModel.LeaveCommand, items[3].Command);
        more.Flyout.Hide();

        fixture.Window.Width = 1600;
        await fixture.Settle();
        Assert.False(panel.IsOverflowing);
    });

    /// <summary>
    /// A speaking participant gets the green frame; it goes away when they stop, and a muted participant never shows it.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task SpeakingParticipant_GetsGreenFrame() => RunAsync(async fixture =>
    {
        Border Frame(string name) => ((Visual)fixture.Tile(name).Parent!).GetVisualChildren().OfType<Border>().Single(b => b.Classes.Contains("speakingFrame"));
        Assert.False(Frame("Марина").IsVisible);

        fixture.Conference.SpeakingChanged += Raise.Event<EventHandler<SpeakingChange>>(fixture.Conference, new SpeakingChange(Guest, true));
        fixture.Conference.SpeakingChanged += Raise.Event<EventHandler<SpeakingChange>>(fixture.Conference, new SpeakingChange(default, true));
        await fixture.Settle();
        Assert.True(Frame("Марина").IsVisible);
        Assert.True(Frame("Вы").IsVisible);
        Assert.Equal(Avalonia.Media.Color.Parse("#62C28E"), ((Avalonia.Media.ISolidColorBrush)Frame("Марина").BorderBrush!).Color);

        fixture.Conference.SpeakingChanged += Raise.Event<EventHandler<SpeakingChange>>(fixture.Conference, new SpeakingChange(Guest, false));
        await fixture.Settle();
        Assert.False(Frame("Марина").IsVisible);

        fixture.GuestMicrophoneOff = true;
        fixture.ViewModel.Refresh();
        fixture.Conference.SpeakingChanged += Raise.Event<EventHandler<SpeakingChange>>(fixture.Conference, new SpeakingChange(Guest, true));
        await fixture.Settle();
        Assert.False(Frame("Марина").IsVisible);
    });

    /// <summary>
    /// The play triangle is drawn around the centre of its icon frame like the pause bars.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task PlayIcon_IsCentered() => RunAsync(fixture =>
    {
        var play = (Avalonia.Media.Geometry)fixture.Window.FindResource("IconPlay")!;
        var pause = (Avalonia.Media.Geometry)fixture.Window.FindResource("IconPause")!;
        Assert.Equal(new Rect(0, 0, 24, 24), play.Bounds);
        Assert.Equal(new Rect(0, 0, 24, 24), pause.Bounds);

        // The centroid of the triangle (8,4.5), (20,12), (8,19.5) is at x = 12, the middle of the frame.
        Assert.True(play.FillContains(new Point(12, 12)));
        Assert.Equal(12, (8 + 20 + 8) / 3.0);
        return Task.CompletedTask;
    });

    /// <summary>
    /// At the end of the film the play button becomes "watch from the beginning" and restarts everybody at zero.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task EndedFilm_OffersReplay() => RunAsync(async fixture =>
    {
        var play = fixture.Window.GetVisualDescendants().OfType<Button>().Single(b => AutomationName(b) == "Пуск или пауза");
        Assert.False(fixture.ViewModel.IsEnded);

        fixture.Ended = true;
        fixture.ViewModel.Refresh();
        await fixture.Settle();
        Assert.True(fixture.ViewModel.IsEnded);
        Assert.Equal("Смотреть сначала", fixture.ViewModel.PlayButtonText);
        var icons = play.GetVisualDescendants().OfType<PathIcon>().Where(i => i.IsVisible).ToArray();
        Assert.Same(fixture.Window.FindResource("IconReplay"), Assert.Single(icons).Data);

        fixture.Click(play, MouseButton.Left);
        await fixture.Settle();
        await fixture.Sessions.Received(1).RequestAsync(
            Arg.Is<PlaybackRequest>(r => r.Kind == PlaybackRequestKind.Play && r.Position == TimeSpan.Zero), Arg.Any<CancellationToken>());
    });

    /// <summary>
    /// After a click on a player button, Space still toggles playback and does not press that button again.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Space_TogglesPlaybackAfterButtonClick() => RunAsync(async fixture =>
    {
        var back = fixture.Window.GetVisualDescendants().OfType<Button>().Single(b => AutomationName(b) == "Назад на 10 секунд");
        fixture.Click(back, MouseButton.Left);
        await fixture.Settle();
        Assert.False(back.IsFocused, "Player buttons do not take the keyboard focus.");
        foreach (var name in new[] { "Пуск или пауза", "Вперёд на 10 секунд", "Звук", "Полный экран" })
        {
            Assert.False(fixture.Window.GetVisualDescendants().OfType<Button>().Single(b => AutomationName(b) == name).Focusable, name);
        }

        await fixture.Sessions.Received(1).RequestAsync(Arg.Is<PlaybackRequest>(r => r.Kind == PlaybackRequestKind.Seek), Arg.Any<CancellationToken>());
        fixture.Sessions.ClearReceivedCalls();

        fixture.Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        fixture.Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        await fixture.Settle();

        await fixture.Sessions.Received(1).RequestAsync(Arg.Is<PlaybackRequest>(r => r.Kind == PlaybackRequestKind.Play), Arg.Any<CancellationToken>());
        await fixture.Sessions.DidNotReceive().RequestAsync(Arg.Is<PlaybackRequest>(r => r.Kind == PlaybackRequestKind.Seek), Arg.Any<CancellationToken>());
    });

    /// <summary>
    /// Typing in the chat sends on Enter and does not control playback; a reaction opens the overlay over the video.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Chat_SendsOnEnterAndShowsReactions() => RunAsync(async fixture =>
    {
        var input = fixture.Window.FindControl<TextBox>("ChatInput")!;
        Assert.True(input.IsEffectivelyVisible);
        input.Focus();
        fixture.Window.KeyTextInput("Всем привет");
        fixture.Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        fixture.Window.KeyTextInput(" ");
        fixture.Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        fixture.Window.KeyTextInput("!");
        await fixture.Settle();
        Assert.Equal("Всем привет !", input.Text);
        await fixture.Sessions.DidNotReceive().RequestAsync(Arg.Any<PlaybackRequest>(), Arg.Any<CancellationToken>());

        fixture.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await fixture.Settle();
        await fixture.Sessions.Received(1).SendChatAsync(ChatKind.Text, "Всем привет !", Arg.Any<CancellationToken>());
        Assert.True(string.IsNullOrEmpty(input.Text));

        // Слой реакций открыт весь сеанс и пуст, пока реакций нет.
        var overlay = fixture.Window.FindControl<Popup>("ReactionOverlay")!;
        Assert.True(overlay.IsOpen);
        Assert.DoesNotContain(overlay.Child!.GetVisualDescendants().OfType<TextBlock>(), t => t.Classes.Contains("emoji"));
        fixture.Sessions.ChatReceived += Raise.Event<EventHandler<ChatEntry>>(
            fixture.Sessions,
            new ChatEntry("01", Guest, "Марина", ChatKind.Reaction, "🔥", DateTimeOffset.Now, false));
        fixture.Sessions.ChatReceived += Raise.Event<EventHandler<ChatEntry>>(
            fixture.Sessions,
            new ChatEntry("02", Guest, "Марина", ChatKind.Text, "Отличная сцена", DateTimeOffset.Now, false));
        await fixture.SettleFor(TimeSpan.FromMilliseconds(400));
        Assert.True(overlay.IsOpen);
        Assert.Contains(overlay.Child!.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "🔥");
        Assert.Contains(fixture.Window.GetVisualDescendants().OfType<SelectableTextBlock>(), t => t.Text == "Отличная сцена");

        var folder = Environment.GetEnvironmentVariable("GOLETHER_TEST_SCREENSHOTS");
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
            using var file = File.Create(Path.Combine(folder, "chat.png"));
            fixture.Window.CaptureRenderedFrame()!.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            if (TopLevel.GetTopLevel(overlay.Child) is { } popupRoot && popupRoot.CaptureRenderedFrame() is { } popupFrame)
            {
                using var popupFile = File.Create(Path.Combine(folder, "reaction.png"));
                popupFrame.Save(popupFile, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
        }

        fixture.ViewModel.Chat.Reactions.Clear();
        await fixture.Settle();
        Assert.DoesNotContain(overlay.Child!.GetVisualDescendants().OfType<TextBlock>(), t => t.Classes.Contains("emoji"));
    });

    /// <summary>
    /// The timeline shows what can be played without waiting: the whole film for the host, the cached parts for a
    /// participant; the participant marker stands exactly over the thumb.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Timeline_ShowsBufferedParts() => RunAsync(async fixture =>
    {
        var timeline = fixture.Window.FindControl<TimelineSlider>("Timeline")!;
        Assert.Equal([new FractionRange(0, 1)], timeline.BufferedRanges);
        Assert.NotNull(timeline.Bar.Parent);
        Assert.Contains(timeline.GetVisualDescendants(), v => ReferenceEquals(v, timeline.Bar));

        fixture.IsHost = false;
        fixture.Buffered = [new MediaTimeRange(TimeSpan.Zero, TimeSpan.FromMinutes(12)), new MediaTimeRange(TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(90))];
        fixture.Position = TimeSpan.FromMinutes(60);
        fixture.ViewModel.Refresh();
        await fixture.SettleFor(TimeSpan.FromMilliseconds(500));
        Assert.Equal([new FractionRange(0, 0.1), new FractionRange(0.5, 0.75)], timeline.BufferedRanges);
        Assert.Same(timeline.BufferedRanges, timeline.Bar.Ranges);
        Assert.True(timeline.Bar.Bounds.Width > 100);

        var thumb = timeline.GetVisualDescendants().OfType<Thumb>().Single();
        var marker = fixture.Window.GetVisualDescendants().OfType<FractionPanel>().Single().Children
            .Single(c => c.DataContext is ParticipantItemViewModel { Name: "Вы" });
        var thumbCenter = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, 0), fixture.Window)!.Value.X;
        var markerCenter = marker.TranslatePoint(new Point(marker.Bounds.Width / 2, 0), fixture.Window)!.Value.X;
        Assert.InRange(markerCenter - thumbCenter, -1.5, 1.5);

        var folder = Environment.GetEnvironmentVariable("GOLETHER_TEST_SCREENSHOTS");
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
            using var file = File.Create(Path.Combine(folder, "timeline.png"));
            fixture.Window.CaptureRenderedFrame()!.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    });

    /// <summary>
    /// The track menu lists the tracks, a click switches one, and the chosen track is marked afterwards.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task TrackMenu_SwitchesTracks() => RunAsync(async fixture =>
    {
        fixture.Tracks =
        [
            new MediaTrack(1, MediaTrackKind.Audio, "Original", "eng", "dts", 6, true, false, true),
            new MediaTrack(2, MediaTrackKind.Audio, "Дубляж", "rus", "ac3", 6, false, false, false),
            new MediaTrack(1, MediaTrackKind.Subtitle, null, "rus", "subrip", null, false, false, false),
        ];
        fixture.ViewModel.Tracks!.Refresh();
        await fixture.Settle();

        var button = fixture.Window.FindControl<Button>("TracksButton")!;
        Assert.True(button.IsVisible);
        button.Flyout!.ShowAt(button);
        await fixture.Settle();
        var entries = fixture.Window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("trackOption")).ToArray();
        var labels = entries.Select(e => (e.DataContext as TrackOptionViewModel)?.Label ?? string.Empty).ToArray();
        Assert.Equal(4, labels.Length);
        Assert.StartsWith("Original · ", labels[0], StringComparison.Ordinal);
        Assert.StartsWith("Дубляж · ", labels[1], StringComparison.Ordinal);
        Assert.Equal("Без субтитров", labels[2]);
        Assert.EndsWith("SUBRIP", labels[3], StringComparison.Ordinal);

        Invoke(entries[1]);
        await fixture.Settle();

        var folder = Environment.GetEnvironmentVariable("GOLETHER_TEST_SCREENSHOTS");
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
            button.Flyout!.ShowAt(button);
            await fixture.SettleFor(TimeSpan.FromMilliseconds(300));
            using var file = File.Create(Path.Combine(folder, "tracks.png"));
            fixture.Window.CaptureRenderedFrame()!.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        fixture.Player.Received(1).SelectTrack(MediaTrackKind.Audio, 2);
        Assert.True(fixture.ViewModel.Tracks.AudioTracks[1].IsSelected, "The chosen track is marked.");
        Assert.False(fixture.ViewModel.Tracks.AudioTracks[0].IsSelected);
    });

    /// <summary>
    /// A reaction floats away and leaves no trace: a later one is alone on the overlay.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Reactions_DoNotPileUp() => RunAsync(async fixture =>
    {
        var overlay = fixture.Window.FindControl<Popup>("ReactionOverlay")!;
        string[] Shown() => overlay.Child is { } child
            ? [.. child.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("emoji")).Select(t => t.Text ?? string.Empty)]
            : [];

        fixture.Sessions.ChatReceived += Raise.Event<EventHandler<ChatEntry>>(
            fixture.Sessions, new ChatEntry("01", Guest, "Марина", ChatKind.Reaction, "🔥", DateTimeOffset.Now, false));
        await fixture.Settle();
        Assert.Single(fixture.ViewModel.Chat.Reactions);
        Assert.Equal(["🔥"], Shown().Distinct());

        // The reaction lives three seconds; after that nothing of it is left.
        await fixture.SettleFor(ChatViewModel.ReactionLifetime + TimeSpan.FromMilliseconds(700));
        Assert.Empty(fixture.ViewModel.Chat.Reactions);
        Assert.Empty(Shown());
        Assert.True(overlay.IsOpen, "Слой висит весь сеанс: закрытое окно не следит за списком.");

        fixture.Sessions.ChatReceived += Raise.Event<EventHandler<ChatEntry>>(
            fixture.Sessions, new ChatEntry("02", Guest, "Марина", ChatKind.Reaction, "❤️", DateTimeOffset.Now, false));
        await fixture.SettleFor(TimeSpan.FromMilliseconds(400));
        Assert.Equal(["❤️"], Shown().Distinct());

        var folder = Environment.GetEnvironmentVariable("GOLETHER_TEST_SCREENSHOTS");
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
            var reactions = fixture.Window.FindControl<Button>("ReactionsButton")!;
            reactions.Flyout!.ShowAt(reactions);
            await fixture.SettleFor(TimeSpan.FromMilliseconds(300));
            using var file = File.Create(Path.Combine(folder, "reactions-menu.png"));
            fixture.Window.CaptureRenderedFrame()!.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    });

    /// <summary>
    /// A weak connection to a participant marks their tile until it recovers.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task WeakConnection_MarksTile() => RunAsync(async fixture =>
    {
        TextBlock Mark(string name) => fixture.Tile(name).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "эконом");
        Assert.False(Mark("Марина").IsEffectivelyVisible);

        fixture.Conference.VideoQualityChanged += Raise.Event<EventHandler<VideoQualityChange>>(fixture.Conference, new VideoQualityChange(Guest, VideoQuality.Low));
        await fixture.Settle();
        Assert.True(Mark("Марина").IsEffectivelyVisible);
        Assert.False(Mark("Вы").IsEffectivelyVisible);

        fixture.Conference.VideoQualityChanged += Raise.Event<EventHandler<VideoQualityChange>>(fixture.Conference, new VideoQualityChange(Guest, VideoQuality.High));
        await fixture.Settle();
        Assert.False(Mark("Марина").IsEffectivelyVisible);
    });

    /// <summary>
    /// A seek moves the timeline and the participant marker smoothly instead of jumping.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Seek_GlidesTimelineAndMarkers() => RunAsync(async fixture =>
    {
        var timeline = fixture.Window.FindControl<Slider>("Timeline")!;
        var marker = fixture.Window.GetVisualDescendants().OfType<FractionPanel>().Single().Children
            .Single(c => c.DataContext is ParticipantItemViewModel { Name: "Вы" });
        await fixture.SettleFor(TimeSpan.FromMilliseconds(500));
        Assert.Equal(60, timeline.Value, 3);
        Assert.Equal(60 / 7200.0, FractionPanel.GetShownFraction(marker)!.Value, 6);

        fixture.Position = TimeSpan.FromMinutes(90);
        fixture.ViewModel.Refresh();
        await fixture.Settle();
        Assert.InRange(timeline.Value, 60, 5400 - 1);
        Assert.InRange(FractionPanel.GetShownFraction(marker)!.Value, 60 / 7200.0, 0.75 - 1e-6);

        await fixture.SettleFor(TimeSpan.FromMilliseconds(500));
        Assert.Equal(5400, timeline.Value, 3);
        Assert.Equal(0.75, FractionPanel.GetShownFraction(marker)!.Value, 6);
    });

    /// <summary>
    /// Renders the window: the rendering must not fail, and with <c>GOLETHER_TEST_SCREENSHOTS</c> set to a folder the
    /// frames are saved for a visual check (narrow window with folded actions, speaking frame, play button).
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Window_Renders() => RunAsync(async fixture =>
    {
        var folder = Environment.GetEnvironmentVariable("GOLETHER_TEST_SCREENSHOTS");
        fixture.Conference.SpeakingChanged += Raise.Event<EventHandler<SpeakingChange>>(fixture.Conference, new SpeakingChange(Guest, true));
        foreach (var width in new[] { 1280, 960 })
        {
            fixture.Window.Width = width;
            fixture.Window.Height = 700;
            await fixture.Settle();
            var frame = fixture.Window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(width, frame.PixelSize.Width);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
                using var file = File.Create(Path.Combine(folder, $"main-{width}.png"));
                frame.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
        }
    });

    /// <summary>
    /// Checks that a control is within the window.
    /// </summary>
    /// <param name="control">The control.</param>
    /// <param name="window">The window.</param>
    /// <returns><see langword="true"/> when the control is inside.</returns>
    private static bool IsInside(Visual control, Window window)
    {
        var topLeft = control.TranslatePoint(default, window)!.Value;
        return topLeft.X >= 0 && topLeft.X + control.Bounds.Width <= window.Bounds.Width + 0.5;
    }

    /// <summary>
    /// Presses a control the way a screen reader or a test automation tool does.
    /// </summary>
    /// <param name="control">The control.</param>
    private static void Invoke(Control control)
    {
        var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(control);
        Assert.NotNull(peer);
        ((Avalonia.Automation.Provider.IInvokeProvider)peer).Invoke();
    }

    /// <summary>
    /// Returns the automation name of a control.
    /// </summary>
    /// <param name="control">The control.</param>
    /// <returns>The name.</returns>
    private static string? AutomationName(Control control) => Avalonia.Automation.AutomationProperties.GetName(control);

    /// <summary>
    /// Runs a test on the UI thread with a fresh window.
    /// </summary>
    /// <param name="test">The test.</param>
    /// <returns>A task that completes when the test is done and fails with the test's exception.</returns>
    private static Task RunAsync(Func<WindowFixture, Task> test)
        => Session.Value.Dispatch(
            () =>
            {
                // The UI loop runs here until the test finishes, so its continuations execute and its failures (or a
                // hang) are reported instead of being lost with an unobserved task.
                var body = RunBodyAsync(test);
                using var done = new CancellationTokenSource();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(done.Token, timeout.Token);
                body.ContinueWith(_ => done.Cancel(), TaskScheduler.Default);
                if (!body.IsCompleted)
                {
                    Dispatcher.UIThread.MainLoop(stop.Token);
                }

                Assert.True(body.IsCompleted, "The UI test did not finish within 60 seconds.");
                body.GetAwaiter().GetResult();
            },
            TestContext.Current.CancellationToken);

    /// <summary>
    /// Creates the window, runs the test and closes the window.
    /// </summary>
    /// <param name="test">The test.</param>
    /// <returns>A task that completes when the test is done.</returns>
    private static async Task RunBodyAsync(Func<WindowFixture, Task> test)
    {
        var fixture = new WindowFixture();
        try
        {
            await fixture.Settle();
            await test(fixture);
        }
        finally
        {
            fixture.Window.Close();
        }
    }

    /// <summary>
    /// A main window in a hosted session with one participant, backed by substitutes.
    /// </summary>
    private sealed class WindowFixture
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WindowFixture"/> class.
        /// </summary>
        public WindowFixture()
        {
            Sessions.GetSnapshot().Returns(_ => Snapshot());
            Settings.GetAsync(MainWindowViewModel.VoiceVolumeSettingPrefix + Guest.Value, Arg.Any<CancellationToken>()).Returns("70");
            Sessions.RequestAsync(default!, default).ReturnsForAnyArgs(Task.CompletedTask);
            var components = Substitute.For<IComponentService>();
            foreach (var id in new[] { ComponentId.Video, ComponentId.Conference })
            {
                components.GetStatus(id).Returns(new ComponentStatus(id, ComponentSource.Bundled, "x", null, null));
            }

            var conference = Conference;
            ViewModel = new MainWindowViewModel(
                Sessions,
                Substitute.For<IDialogService>(),
                Settings,
                new InlineDispatcher(),
                _ => throw new NotSupportedException(),
                "A249-B9CC",
                conference,
                components,
                Player);
            Window = new MainWindow { Width = 1280, Height = 780 };
            Window.Initialize(ViewModel, new PlayerHost(NullLoggerFactory.Instance, Path.GetTempPath()), conference);
            Window.Show();
            ViewModel.Refresh();
        }

        /// <summary>
        /// Gets the session service.
        /// </summary>
        public ISessionService Sessions { get; } = Substitute.For<ISessionService>();

        /// <summary>
        /// Gets the settings.
        /// </summary>
        public ISettingsStore Settings { get; } = Substitute.For<ISettingsStore>();

        /// <summary>
        /// Gets or sets a value indicating whether this device hosts the session.
        /// </summary>
        public bool IsHost { get; set; } = true;

        /// <summary>
        /// Gets the conferencing backend.
        /// </summary>
        public IConferenceMedia Conference { get; } = Substitute.For<IConferenceMedia>();

        /// <summary>
        /// Gets the local player controls.
        /// </summary>
        public ILocalPlayerControls Player { get; } = Substitute.For<ILocalPlayerControls>();

        /// <summary>
        /// Gets the view model.
        /// </summary>
        public MainWindowViewModel ViewModel { get; }

        /// <summary>
        /// Gets the window.
        /// </summary>
        public MainWindow Window { get; }

        /// <summary>
        /// Gets or sets a value indicating whether the participant reports the microphone as off.
        /// </summary>
        public bool GuestMicrophoneOff { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the film has ended.
        /// </summary>
        public bool Ended { get; set; }

        /// <summary>
        /// Gets or sets the position reported by the local player.
        /// </summary>
        public TimeSpan Position { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Gets or sets the parts cached by the local player.
        /// </summary>
        public IReadOnlyList<MediaTimeRange>? Buffered { get; set; }

        /// <summary>
        /// Gets or sets the tracks the player reports.
        /// </summary>
        public IReadOnlyList<MediaTrack> Tracks
        {
            get => _tracks;
            set
            {
                _tracks = value;
                Player.GetTracks().Returns(_ => _tracks);
            }
        }

        /// <summary>
        /// The tracks of the player.
        /// </summary>
        private IReadOnlyList<MediaTrack> _tracks = [];

        /// <summary>
        /// Lets bindings, layout and rendering catch up.
        /// </summary>
        /// <returns>A task that completes when the UI is idle.</returns>
        public async Task Settle()
        {
            for (var i = 0; i < 3; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                await Task.Yield();
            }
        }

        /// <summary>
        /// Keeps rendering frames for a while (animations run on the real clock).
        /// </summary>
        /// <param name="duration">How long.</param>
        /// <returns>A task that completes after the duration.</returns>
        public async Task SettleFor(TimeSpan duration)
        {
            var deadline = DateTime.UtcNow + duration;
            while (DateTime.UtcNow < deadline)
            {
                await Settle();
                await Task.Delay(15);
            }

            await Settle();
        }

        /// <summary>
        /// Finds the tile of a participant by name.
        /// </summary>
        /// <param name="name">The display name.</param>
        /// <returns>The tile border.</returns>
        public Border Tile(string name)
            => Window.GetVisualDescendants().OfType<Border>()
                .Single(b => b.Classes.Contains("tile") && b.DataContext is ParticipantItemViewModel item && item.Name == name);

        /// <summary>
        /// Returns the window point in the middle of a control.
        /// </summary>
        /// <param name="control">The control.</param>
        /// <returns>The point.</returns>
        public Point PointOf(Visual control)
            => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Window)!.Value;

        /// <summary>
        /// Clicks a control.
        /// </summary>
        /// <param name="control">The control.</param>
        /// <param name="button">The button.</param>
        public void Click(Visual control, MouseButton button)
        {
            var point = PointOf(control);
            Window.MouseMove(point);
            Window.MouseDown(point, button);
            Window.MouseUp(point, button);
            Dispatcher.UIThread.RunJobs();
        }

        /// <summary>
        /// Moves the pointer over a control.
        /// </summary>
        /// <param name="control">The control.</param>
        public void Move(Visual control) => Window.MouseMove(PointOf(control));

        /// <summary>
        /// Simulates a mouse action reported by the video player.
        /// </summary>
        /// <param name="action">The action.</param>
        public void VideoPointer(VideoPointerAction action)
            => typeof(MainWindow).GetMethod("OnVideoPointer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Window, [action]);

        /// <summary>
        /// Builds the session snapshot.
        /// </summary>
        /// <returns>The snapshot.</returns>
        private SessionSnapshot Snapshot()
        {
            var player = new PlayerSnapshot(true, Position, TimeSpan.FromHours(2), true, false, TimeSpan.FromSeconds(20), 1.0) { Buffered = Buffered };
            return new SessionSnapshot
            {
                IsHost = IsHost,
                State = SessionState.Active,
                SessionName = "Вечер кино",
                HostPeerId = Host,
                Participants =
                [
                    new ParticipantView(new ParticipantInfo(Host, "Вы", true), new ParticipantStatus { PeerId = Host, Position = Position }, true),
                    new ParticipantView(new ParticipantInfo(Guest, "Марина", false), new ParticipantStatus { PeerId = Guest, MicrophoneOff = GuestMicrophoneOff }, false),
                ],
                Playback = PlaybackState.Initial(Host, 0) with
                {
                    State = PlayState.Paused,
                    Position = Ended ? TimeSpan.FromHours(2) : TimeSpan.FromMinutes(1),
                    Cause = Ended ? PlaybackCause.Ended : PlaybackCause.Pause,
                },
                Media = new MediaDescriptor { FileName = "Dune.mkv", Length = 1024 * 1024, QuickId = new string('c', 64) },
                Local = new Sync.Engine.FollowerStatus(player, TimeSpan.FromMinutes(1), TimeSpan.Zero, null),
            };
        }
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
