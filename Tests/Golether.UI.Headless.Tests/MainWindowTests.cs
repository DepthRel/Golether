using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Golether.Components.Installation;
using Golether.Core.Data.Enums;
using Golether.Core.Data.Stores;
using Golether.Core.Enums;
using Golether.Core.Identity;
using Golether.Core.Media;
using Golether.Core.Playback;
using Golether.Core.Session;
using Golether.Media.Conference;
using Golether.Media.Player;
using Golether.Session;
using Golether.Sync.Protocol;
using Golether.UI.Controls;
using Golether.UI.Enums;
using Golether.UI.Services;
using Golether.UI.ViewModels;
using Golether.UI.Views;

namespace Golether.UI.Headless.Tests;

/// <summary>
/// Tests of the main window: the participant menu, the event feed, the volume pop-up and clicks on the video.
/// </summary>
public sealed class MainWindowTests
{
    /// <summary>
    /// The headless session shared by the tests (Avalonia starts once per process).
    /// </summary>
    private static readonly Lazy<HeadlessUnitTestSession> _session = new(() => HeadlessUnitTestSession.StartNew(typeof(TestApp)));

    /// <summary>
    /// Initializes static members of the <see cref="MainWindowTests"/> class. The toolbar folds without its
    /// animation, so the tests see the settled row instead of a frame in the middle of it.
    /// </summary>
    static MainWindowTests()
    {
        OverflowPanel.FoldDuration = TimeSpan.Zero;
        SlideReveal.Duration = TimeSpan.Zero;
    }

    /// <summary>
    /// This device, the host.
    /// </summary>
    private static readonly PeerId _host = PeerId.Parse(new string('a', 64));

    /// <summary>
    /// A participant.
    /// </summary>
    private static readonly PeerId _guest = PeerId.Parse(new string('b', 64));

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
        Assert.Equal([null, "Обычная громкость (100 %)", "Вывести в отдельное окно", "Выключить микрофон", "Выключить камеру", "Исключить из сеанса"], texts);
        Assert.All(items, i => Assert.True(i.IsVisible));
        Assert.DoesNotContain(texts, t => t?.Contains("Включить", StringComparison.Ordinal) == true);

        items[3].Command!.Execute(null);
        await fixture.Settle();
        await fixture.Sessions.Received(1).SwitchOffParticipantDevicesAsync(_guest, true, false);

        // The voice volume changes only here and is remembered for this participant. Louder than the original only
        // clips the voice, so the slider stops at 100 %.
        var slider = menu.GetLogicalDescendants().OfType<Slider>().Single();
        Assert.Equal((0d, 100d, 70d), (slider.Minimum, slider.Maximum, slider.Value));

        var folder = Environment.GetEnvironmentVariable("GOLETHER_TEST_SCREENSHOTS");
        if (!string.IsNullOrEmpty(folder) && TopLevel.GetTopLevel(slider) is { } menuRoot && menuRoot.CaptureRenderedFrame() is { } frame)
        {
            Directory.CreateDirectory(folder);
            using var file = File.Create(Path.Combine(folder, "participant-menu.png"));
            frame.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        slider.Value = 50;
        await fixture.Settle();
        fixture.Conference.Received().SetVoiceVolume(_guest, 0.5);

        // Muting is the speaker next to the slider, not a menu entry of its own.
        var mute = menu.GetLogicalDescendants().OfType<Button>().Single(b => b.Classes.Contains("mute"));
        mute.Command!.Execute(null);
        await fixture.Settle();
        fixture.Conference.Received().SetVoiceVolume(_guest, 0);
        Assert.Equal("Вернуть звук", ToolTip.GetTip(mute));
        await Task.Delay(MainWindowViewModel.VoiceVolumeSaveDelay + TimeSpan.FromMilliseconds(300));
        await fixture.Settings.Received(1).SetAsync(MainWindowViewModel.VOICE_VOLUME_SETTING_PREFIX + _guest.Value, "0", Arg.Any<CancellationToken>());
        menu.Close();

        // A microphone that is already off cannot be switched off again.
        fixture.GuestMicrophoneOff = true;
        fixture.ViewModel.Refresh();
        await fixture.Settle();
        Assert.False(items[3].Command!.CanExecute(null));

        // A participant (not the host) gets the volume and the separate window, but neither the switch-off commands
        // nor the removal.
        fixture.IsHost = false;
        fixture.ViewModel.Refresh();
        await fixture.Settle();
        fixture.Click(guestTile, MouseButton.Right);
        await fixture.Settle();
        Assert.True(menu.IsOpen);
        Assert.Equal([true, true, true, false, false, false], items.Select(i => i.IsVisible));
        menu.Close();
    });

    /// <summary>
    /// The menu of a participant opens their camera in a window of its own, once; closing it changes nothing, and
    /// the window goes away with the participant.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task ParticipantMenu_OpensTheCameraInASeparateWindow() => RunAsync(async fixture =>
    {
        var tile = fixture.Tile("Марина");
        fixture.Click(tile, MouseButton.Right);
        await fixture.Settle();
        var entry = tile.ContextMenu!.Items.OfType<MenuItem>().Single(i => i.Header as string == "Вывести в отдельное окно");

        entry.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        await fixture.Settle();
        var window = Assert.Single(fixture.Window.ParticipantWindows);
        Assert.True(window.IsVisible);
        Assert.Equal("Марина", window.Title);
        Assert.Same(fixture.ViewModel.Participants.Single(p => p.PeerId == _guest), window.DataContext);
        Assert.True(window.CanResize);

        // Asking again brings the same window forward instead of opening another one.
        entry.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        await fixture.Settle();
        Assert.Same(window, Assert.Single(fixture.Window.ParticipantWindows));

        // Closing it is not an action of the session.
        window.Close();
        await fixture.Settle();
        Assert.Empty(fixture.Window.ParticipantWindows);
        Assert.Equal(2, fixture.ViewModel.Participants.Count);
        fixture.Conference.DidNotReceiveWithAnyArgs().SetMuted(default, default);

        // A window that is open when the participant leaves closes by itself.
        entry.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        await fixture.Settle();
        var second = Assert.Single(fixture.Window.ParticipantWindows);
        fixture.GuestPresent = false;
        fixture.ViewModel.Refresh();
        await fixture.Settle();
        Assert.False(second.IsVisible);
        Assert.Empty(fixture.Window.ParticipantWindows);
    });

    /// <summary>
    /// The border between the video and the participants shows only with the adjustable layout; a wider panel makes
    /// the camera tiles larger; a double click on the border and switching the layout off both restore the designed
    /// width.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task LayoutSplitter_ResizesThePanelAndResets() => RunAsync(async fixture =>
    {
        var splitter = fixture.Window.FindControl<GridSplitter>("LayoutSplitter")!;
        var grid = fixture.Window.FindControl<Grid>("SessionGrid")!;
        var picture = fixture.Tile("Марина").GetVisualDescendants().OfType<AspectPanel>().Single();
        double PanelWidth() => grid.ColumnDefinitions[2].ActualWidth;

        Assert.False(splitter.IsVisible);
        Assert.Equal(300, PanelWidth());
        Assert.Equal(154, picture.Bounds.Height, 0);

        fixture.ViewModel.SetLayoutResizable(true);
        await fixture.Settle();
        Assert.True(splitter.IsVisible);
        Assert.Equal(300, PanelWidth());

        fixture.ViewModel.StoreSidePanelWidth(450);
        await fixture.Settle();
        Assert.Equal(450, PanelWidth());
        Assert.InRange(picture.Bounds.Height, 234, 242);

        // More than the window has room for: the video keeps its minimum.
        fixture.ViewModel.StoreSidePanelWidth(5000);
        await fixture.Settle();
        Assert.Equal(fixture.Window.Bounds.Width - splitter.Bounds.Width - MainWindowViewModel.MIN_VIDEO_WIDTH, PanelWidth(), 0);

        var point = fixture.PointOf(splitter);
        fixture.Window.MouseDown(point, MouseButton.Left);
        fixture.Window.MouseUp(point, MouseButton.Left);
        fixture.Window.MouseDown(point, MouseButton.Left);
        fixture.Window.MouseUp(point, MouseButton.Left);
        await fixture.Settle();
        Assert.Equal(300, fixture.ViewModel.SidePanelWidth);
        Assert.Equal(300, PanelWidth());

        fixture.ViewModel.StoreSidePanelWidth(450);
        fixture.ViewModel.SetLayoutResizable(false);
        await fixture.Settle();
        Assert.False(splitter.IsVisible);
        Assert.Equal(300, PanelWidth());
        Assert.Equal(154, picture.Bounds.Height, 0);
    });

    /// <summary>
    /// A double click on the video area toggles full screen also while no video is loaded there.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task DoubleClick_TogglesFullScreenWithoutVideo() => RunAsync(async fixture =>
    {
        var area = fixture.Window.FindControl<Panel>("VideoArea")!;
        Assert.False(fixture.ViewModel.ShowVideo, "The test needs the placeholder, not the video.");

        void DoubleClick()
        {
            var point = area.TranslatePoint(new Point(30, 30), fixture.Window)!.Value;
            fixture.Window.MouseDown(point, MouseButton.Left);
            fixture.Window.MouseUp(point, MouseButton.Left);
            fixture.Window.MouseDown(point, MouseButton.Left);
            fixture.Window.MouseUp(point, MouseButton.Left);
        }

        DoubleClick();
        await fixture.Settle();
        Assert.Equal(WindowState.FullScreen, fixture.Window.WindowState);

        DoubleClick();
        await fixture.Settle();
        Assert.NotEqual(WindowState.FullScreen, fixture.Window.WindowState);
    });

    /// <summary>
    /// The cursor disappears over the resting video in any window state. Full screen hides the status bar and moves
    /// the top bar and the player bar into layers over the video: they leave after the pointer left them, come back
    /// at the top or bottom edge and stay while the pointer is on them — and the video never changes its size for
    /// it. "Always show the timeline" puts the bars back into the layout, with the video between them.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task FullScreen_LaysTheBarsOverTheVideoAndHidesThem()
    {
        var delay = MainWindow.ChromeDelay;
        MainWindow.ChromeDelay = TimeSpan.FromMilliseconds(400);
        try
        {
            await RunAsync(async fixture =>
            {
                var top = fixture.Window.FindControl<SlideReveal>("TopBar")!;
                var player = fixture.Window.FindControl<SlideReveal>("PlayerBar")!;
                var topLayer = fixture.Window.FindControl<Popup>("TopBarPopup")!;
                var playerLayer = fixture.Window.FindControl<Popup>("PlayerBarPopup")!;
                var topSlot = fixture.Window.FindControl<Decorator>("TopBarSlot")!;
                var status = fixture.Window.FindControl<Border>("StatusBar")!;
                var area = fixture.Window.FindControl<Panel>("VideoArea")!;
                var wait = MainWindow.ChromeDelay + TimeSpan.FromMilliseconds(700);
                Assert.True(status.IsVisible);
                Assert.Same(top, topSlot.Child);
                Assert.True(top.Bounds.Height > 20);
                Assert.True(player.Bounds.Height > 20);
                var windowed = area.Bounds.Height;

                // In a plain window the cursor hides over the resting video, and the bars stay where they are.
                fixture.Window.MouseMove(fixture.PointOf(area));
                await fixture.SettleFor(wait);
                Assert.NotNull(area.Cursor);
                Assert.True(top.IsExpanded);
                Assert.False(topLayer.IsOpen);
                Assert.Equal(windowed, area.Bounds.Height);
                fixture.Window.MouseMove(fixture.PointOf(area) + new Vector(5, 5));
                await fixture.Settle();
                Assert.Null(area.Cursor);

                // Full screen: the bars leave the layout, the video takes the whole height and keeps it.
                fixture.Window.WindowState = WindowState.FullScreen;
                await fixture.Settle();
                Assert.False(status.IsVisible);
                Assert.Null(topSlot.Child);
                Assert.True(topLayer.IsOpen);
                Assert.True(playerLayer.IsOpen);
                var full = area.Bounds.Height;
                Assert.True(full > windowed + 40, "The video takes the room of the bars.");
                Assert.Equal(fixture.Window.Bounds.Height, full);

                fixture.Window.MouseMove(fixture.PointOf(area) + new Vector(3, 3));
                await fixture.SettleFor(wait);
                Assert.False(top.IsExpanded);
                Assert.False(topLayer.IsOpen);
                Assert.False(playerLayer.IsOpen);
                Assert.NotNull(area.Cursor);
                Assert.Equal(full, area.Bounds.Height);

                // A movement over the video shows the cursor and leaves the bars hidden.
                fixture.Window.MouseMove(fixture.PointOf(area) + new Vector(9, 9));
                await fixture.Settle();
                Assert.Null(area.Cursor);
                Assert.False(topLayer.IsOpen);

                // Over the participants the cursor stays, and the bars stay hidden.
                fixture.Window.MouseMove(fixture.PointOf(fixture.Tile("Марина")));
                await fixture.SettleFor(wait);
                Assert.Null(area.Cursor);
                Assert.False(topLayer.IsOpen);

                // The top edge brings both bars back over the video, which keeps its size.
                fixture.Window.MouseMove(new Point(200, 2));
                await fixture.Settle();
                Assert.True(topLayer.IsOpen);
                Assert.True(playerLayer.IsOpen);
                Assert.True(top.IsExpanded);
                Assert.True(top.Bounds.Height > 20);
                Assert.Equal(fixture.Window.Bounds.Width, top.Bounds.Width);
                Assert.Equal(area.Bounds.Width, player.Bounds.Width);

                // The top bar lies at the top of the window, the player bar at the bottom of the video; neither
                // layer may be moved to keep it inside the working area of the screen.
                var layer = TopLevel.GetTopLevel(player)!;
                Assert.Equal(0, top.TranslatePoint(default, TopLevel.GetTopLevel(top)!)!.Value.Y, 0);
                Assert.Equal(fixture.Window.Bounds.Height, player.TranslatePoint(new Point(0, player.Bounds.Height), layer)!.Value.Y, 0);
                Assert.Equal(Avalonia.Controls.Primitives.PopupPositioning.PopupPositionerConstraintAdjustment.None, topLayer.PlacementConstraintAdjustment);
                Assert.Equal(Avalonia.Controls.Primitives.PopupPositioning.PopupPositionerConstraintAdjustment.None, playerLayer.PlacementConstraintAdjustment);
                Assert.Equal(full, area.Bounds.Height);

                // They stay while the pointer is on them. On a desktop the layer is a window of its own, so the
                // pointer is sent to whatever hosts the bar.
                var layerRoot = TopLevel.GetTopLevel(top)!;
                layerRoot.MouseMove(top.TranslatePoint(new Point(120, 10), layerRoot)!.Value);
                await fixture.SettleFor(wait);
                Assert.True(topLayer.IsOpen);

                // So does the bottom edge under the video, after they left again.
                fixture.Window.MouseMove(fixture.PointOf(area));
                await fixture.SettleFor(wait);
                Assert.False(playerLayer.IsOpen);
                fixture.Window.MouseMove(new Point(200, fixture.Window.Bounds.Height - 2));
                await fixture.Settle();
                Assert.True(playerLayer.IsOpen);

                // An open menu of the bar keeps them.
                var reactions = fixture.Window.FindControl<Button>("ReactionsButton")!;
                reactions.Flyout!.ShowAt(reactions);
                fixture.Window.MouseMove(fixture.PointOf(area));
                await fixture.SettleFor(wait);
                Assert.True(playerLayer.IsOpen);
                reactions.Flyout.Hide();
                await fixture.SettleFor(wait);
                Assert.False(playerLayer.IsOpen);

                // The shortcuts of the window work whatever the bars do.
                fixture.Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                fixture.Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                await fixture.Settle();
                await fixture.Sessions.Received(1).RequestAsync(Arg.Is<PlaybackRequest>(r => r.Kind == PlaybackRequestKind.Play), Arg.Any<CancellationToken>());

                // "Always show the timeline": the bars are back in the layout and do not leave.
                fixture.ViewModel.SetAlwaysShowBars(true);
                await fixture.SettleFor(wait);
                Assert.Same(top, topSlot.Child);
                Assert.False(topLayer.IsOpen);
                Assert.False(playerLayer.IsOpen);
                Assert.True(top.IsExpanded);
                Assert.Equal(1, top.Shown);
                Assert.True(player.Bounds.Height > 20);
                Assert.True(area.Bounds.Height < full - 40, "The video fits between the bars.");
                Assert.False(status.IsVisible);

                fixture.ViewModel.SetAlwaysShowBars(false);
                fixture.Window.WindowState = WindowState.Normal;
                await fixture.Settle();
                Assert.True(status.IsVisible);
                Assert.Same(top, topSlot.Child);
                Assert.True(top.IsExpanded);
                Assert.True(player.IsExpanded);
                Assert.Equal(windowed, area.Bounds.Height);
            });
        }
        finally
        {
            MainWindow.ChromeDelay = delay;
        }
    }

    /// <summary>
    /// Up to three participants keep one column of tiles however wide the panel is; more of them go in two columns
    /// once the panel is more than three times as wide as designed.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Tiles_GoInTwoColumnsWhenManyAndWide() => RunAsync(async fixture =>
    {
        var tiles = fixture.Window.FindControl<ItemsControl>("ParticipantList")!;
        UniformGrid Grid() => (UniformGrid)tiles.ItemsPanelRoot!;
        fixture.Window.Width = 1700;
        fixture.ViewModel.SetLayoutResizable(true);
        fixture.ViewModel.StoreSidePanelWidth(1000);
        await fixture.Settle();
        Assert.Equal(1, Grid().Columns);
        var single = fixture.Tile("Марина").Bounds.Width;

        fixture.ExtraGuests = 2;
        fixture.ViewModel.Refresh();
        await fixture.Settle();
        Assert.Equal(4, fixture.ViewModel.Participants.Count);
        Assert.Equal(2, Grid().Columns);
        var first = fixture.Tile("Вы");
        var second = fixture.Tile("Марина");
        Assert.Equal(fixture.PointOf(first).Y, fixture.PointOf(second).Y, 0);
        Assert.True(fixture.PointOf(second).X > fixture.PointOf(first).X + 100);
        Assert.InRange(second.Bounds.Width, (single / 2) - 10, single / 2);
        fixture.Shot("tiles-two-columns");

        // A panel that is not wide enough keeps the single column.
        fixture.ViewModel.StoreSidePanelWidth(600);
        await fixture.Settle();
        Assert.Equal(1, Grid().Columns);
    });

    /// <summary>
    /// With the adjustable layout a tile dragged onto another one swaps with it; a locked layout leaves the tiles
    /// alone, and a plain click on a tile moves nothing.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Tiles_SwapByDragging() => RunAsync(async fixture =>
    {
        string[] Order() => [.. fixture.ViewModel.Participants.Select(p => p.Name)];
        Border Veil(string name) => ((Visual)fixture.Tile(name).Parent!).GetVisualChildren().OfType<Border>().Single(b => b.Classes.Contains("dragVeil"));
        void Drag(string from, string to, bool drop = true)
        {
            var start = fixture.PointOf(fixture.Tile(from));
            var end = fixture.PointOf(fixture.Tile(to));
            fixture.Window.MouseMove(start);
            fixture.Window.MouseDown(start, MouseButton.Left);
            fixture.Window.MouseMove(start + new Vector(0, 12));
            fixture.Window.MouseMove(end);
            if (drop)
            {
                fixture.Window.MouseUp(end, MouseButton.Left);
            }
        }

        // The layout is locked: nothing moves.
        Drag("Вы", "Марина");
        await fixture.Settle();
        Assert.Equal(["Вы", "Марина"], Order());

        fixture.ViewModel.SetLayoutResizable(true);
        await fixture.Settle();
        fixture.Click(fixture.Tile("Вы"), MouseButton.Left);
        await fixture.Settle();
        Assert.Equal(["Вы", "Марина"], Order());

        // While the tile is carried, it and the tile under the pointer are marked.
        Drag("Вы", "Марина", drop: false);
        await fixture.Settle();
        Assert.True(Veil("Вы").IsVisible, "The carried tile is dimmed.");
        Assert.False(Veil("Марина").IsVisible);
        Assert.Equal(1, fixture.Tile("Вы").Opacity);
        Assert.Contains("dropTarget", fixture.Tile("Марина").Classes);
        fixture.Window.MouseUp(fixture.PointOf(fixture.Tile("Марина")), MouseButton.Left);
        await fixture.Settle();
        Assert.Equal(["Марина", "Вы"], Order());
        Assert.False(Veil("Вы").IsVisible);
        Assert.DoesNotContain("dropTarget", fixture.Tile("Марина").Classes);
        Assert.True(fixture.PointOf(fixture.Tile("Марина")).Y < fixture.PointOf(fixture.Tile("Вы")).Y);

        // Switching the adjustable layout off restores the order of the session.
        fixture.ViewModel.SetLayoutResizable(false);
        await fixture.Settle();
        Assert.Equal(["Вы", "Марина"], Order());
    });

    /// <summary>
    /// A press a little above or below the line of the timeline seeks exactly like a press on the line, and a drag
    /// from there moves the thumb along; the user does not have to aim at the line.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Timeline_SeeksFromAboveAndBelowTheLine() => RunAsync(async fixture =>
    {
        var timeline = fixture.Window.FindControl<TimelineSlider>("Timeline")!;
        await fixture.SettleFor(TimeSpan.FromMilliseconds(400));
        var origin = timeline.TranslatePoint(default, fixture.Window)!.Value;
        var x = timeline.Bounds.Width * 0.4;

        async Task<double> SeekAt(double y)
        {
            fixture.Sessions.ClearReceivedCalls();
            var point = origin + new Vector(x, y);
            fixture.Window.MouseMove(point);
            fixture.Window.MouseDown(point, MouseButton.Left);
            await fixture.Settle();
            Assert.True(fixture.ViewModel.IsScrubbing);
            var value = timeline.Value;
            fixture.Window.MouseUp(point, MouseButton.Left);
            await fixture.Settle();
            await fixture.Sessions.Received(1).RequestAsync(
                Arg.Is<PlaybackRequest>(r => r.Kind == PlaybackRequestKind.Seek && Math.Abs(r.Position!.Value.TotalSeconds - value) < 1),
                Arg.Any<CancellationToken>());
            return value;
        }

        // The line itself is in the middle of the slider; its top and bottom strips used to do nothing.
        var onLine = await SeekAt(timeline.Bounds.Height / 2);
        Assert.InRange(onLine, 7200 * 0.38, 7200 * 0.42);
        Assert.Equal(onLine, await SeekAt(3), 3);
        Assert.Equal(onLine, await SeekAt(timeline.Bounds.Height - 3), 3);

        // A drag that starts above the line carries the thumb.
        fixture.Sessions.ClearReceivedCalls();
        var start = origin + new Vector(x, 3);
        fixture.Window.MouseDown(start, MouseButton.Left);
        fixture.Window.MouseMove(start + new Vector(timeline.Bounds.Width * 0.3, 0));
        await fixture.Settle();
        Assert.InRange(timeline.Value, 7200 * 0.68, 7200 * 0.72);
        fixture.Window.MouseUp(start + new Vector(timeline.Bounds.Width * 0.3, 0), MouseButton.Left);
        await fixture.Settle();
        Assert.False(fixture.ViewModel.IsScrubbing);
        await fixture.Sessions.Received(1).RequestAsync(Arg.Is<PlaybackRequest>(r => r.Kind == PlaybackRequestKind.Seek), Arg.Any<CancellationToken>());
    });

    /// <summary>
    /// A stored voice volume is applied when the participant appears.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task StoredVoiceVolume_IsApplied() => RunAsync(async fixture =>
    {
        await fixture.Settle();
        var tile = fixture.ViewModel.Participants.Single(p => p.PeerId == _guest);
        Assert.Equal(70, tile.VoiceVolume);
        fixture.Conference.Received().SetVoiceVolume(_guest, 0.7);
        fixture.Conference.DidNotReceive().SetVoiceVolume(_host, Arg.Any<double>());
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
        var leave = panel.Children.OfType<Button>().Single(b => b.Content as string == "Завершить");

        fixture.Window.Width = 1600;
        await fixture.Settle();
        Assert.False(panel.IsOverflowing);
        Assert.DoesNotContain(OverflowPanel.OVERFLOWED_CLASS, leave.Classes);
        Assert.Contains(OverflowPanel.OVERFLOWED_CLASS, more.Classes);
        Assert.True(IsInside(leave, fixture.Window));

        // Over the video the bar holds fewer buttons (no tunnels, no language), so at the narrowest window it still fits;
        // a long session name or a long translation crowds it, which is simulated by limiting the width of the bar.
        fixture.Window.Width = 960;
        panel.MaxWidth = 380;
        await fixture.Settle();
        Assert.True(panel.IsOverflowing);
        Assert.Contains(OverflowPanel.OVERFLOWED_CLASS, leave.Classes);
        Assert.Equal(0, leave.Bounds.Width);
        Assert.DoesNotContain(OverflowPanel.OVERFLOWED_CLASS, more.Classes);
        Assert.True(IsInside(more, fixture.Window), "The more button stays inside the window.");
        foreach (var child in panel.Children.Where(c => c.Bounds.Width > 0))
        {
            Assert.True(IsInside(child, fixture.Window), $"{child} leaves the window.");
        }

        more.Flyout!.ShowAt(more);
        await fixture.Settle();
        var items = ((MenuFlyout)more.Flyout).Items.OfType<MenuItem>().ToArray();
        // Settings is available everywhere, so its menu item shows over the video too.
        Assert.True(items[0].IsVisible);
        Assert.Equal(["Настройки", "Пригласить", "Отчёт для диагностики…", "Завершить"], items.Select(i => (string)i.Header!));
        Assert.Same(fixture.ViewModel.LeaveCommand, items[3].Command);
        more.Flyout.Hide();

        fixture.Window.Width = 1600;
        panel.MaxWidth = double.PositiveInfinity;
        await fixture.Settle();
        Assert.False(panel.IsOverflowing);
    });

    /// <summary>
    /// The row is laid out from the fold of its items, which is what the folding animation drives: half folded is half
    /// the width and half the opacity, so the neighbours glide along instead of jumping.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task ToolbarFold_DrivesWidthAndOpacity() => RunAsync(async fixture =>
    {
        var panel = fixture.Window.FindControl<OverflowPanel>("TopActions")!;
        var leave = panel.Children.OfType<Button>().Single(b => b.Content as string == "Завершить");
        fixture.Window.Width = 1600;
        await fixture.Settle();
        var full = leave.Bounds.Width;
        Assert.True(full > 0, "The button is shown to begin with.");

        OverflowPanel.SetFold(leave, 0.5);
        await fixture.Settle();
        Assert.InRange(leave.Bounds.Width, full * 0.4, full * 0.6);
        Assert.Equal(0.5, leave.Opacity, 3);

        OverflowPanel.SetFold(leave, 1);
        await fixture.Settle();
        Assert.Equal(0, leave.Bounds.Width);
        Assert.Equal(0, leave.Opacity, 3);
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

        fixture.Conference.SpeakingChanged += Raise.Event<EventHandler<SpeakingChange>>(fixture.Conference, new SpeakingChange(_guest, true));
        fixture.Conference.SpeakingChanged += Raise.Event<EventHandler<SpeakingChange>>(fixture.Conference, new SpeakingChange(default, true));
        await fixture.Settle();
        Assert.True(Frame("Марина").IsVisible);
        Assert.True(Frame("Вы").IsVisible);
        Assert.Equal(Avalonia.Media.Color.Parse("#62C28E"), ((Avalonia.Media.ISolidColorBrush)Frame("Марина").BorderBrush!).Color);

        fixture.Conference.SpeakingChanged += Raise.Event<EventHandler<SpeakingChange>>(fixture.Conference, new SpeakingChange(_guest, false));
        await fixture.Settle();
        Assert.False(Frame("Марина").IsVisible);

        fixture.GuestMicrophoneOff = true;
        fixture.ViewModel.Refresh();
        fixture.Conference.SpeakingChanged += Raise.Event<EventHandler<SpeakingChange>>(fixture.Conference, new SpeakingChange(_guest, true));
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
            new ChatEntry("01", _guest, "Марина", ChatKind.Reaction, "🔥", DateTimeOffset.Now, false));
        fixture.Sessions.ChatReceived += Raise.Event<EventHandler<ChatEntry>>(
            fixture.Sessions,
            new ChatEntry("02", _guest, "Марина", ChatKind.Text, "Отличная сцена", DateTimeOffset.Now, false));
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

        // The mark is the chosen avatar when there is one, and the coloured initial otherwise.
        var you = (ParticipantItemViewModel)marker.DataContext!;
        Assert.DoesNotContain(marker.GetVisualDescendants().OfType<Image>(), image => image.IsEffectivelyVisible);
        var avatar = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(8, 8), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
        you.AvatarImage = avatar;
        await fixture.Settle();
        Assert.Contains(marker.GetVisualDescendants().OfType<Image>(), image => image.IsEffectivelyVisible && ReferenceEquals(image.Source, avatar));

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
            fixture.Sessions, new ChatEntry("01", _guest, "Марина", ChatKind.Reaction, "🔥", DateTimeOffset.Now, false));
        await fixture.Settle();
        Assert.Single(fixture.ViewModel.Chat.Reactions);
        Assert.Equal(["🔥"], Shown().Distinct());

        // The reaction lives three seconds; after that nothing of it is left.
        await fixture.SettleFor(ChatViewModel.ReactionLifetime + TimeSpan.FromMilliseconds(700));
        Assert.Empty(fixture.ViewModel.Chat.Reactions);
        Assert.Empty(Shown());
        Assert.True(overlay.IsOpen, "Слой висит весь сеанс: закрытое окно не следит за списком.");

        fixture.Sessions.ChatReceived += Raise.Event<EventHandler<ChatEntry>>(
            fixture.Sessions, new ChatEntry("02", _guest, "Марина", ChatKind.Reaction, "❤️", DateTimeOffset.Now, false));
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
    /// The pen button presses in and out; while it is pressed a drag over the video draws a stroke and sends it, and
    /// a stroke of another participant appears over the picture in their own colour.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Pen_DrawsOverTheVideo() => RunAsync(async fixture =>
    {
        var pen = fixture.Window.FindControl<ToggleButton>("PenButton")!;
        Assert.True(pen.IsEffectivelyVisible);
        Assert.False(fixture.ViewModel.Drawing.IsPenActive);

        // Отжатая кнопка выглядит ровно как соседние: прозрачный фон, та же рамка.
        var reactions = fixture.Window.FindControl<Button>("ReactionsButton")!;
        Assert.Equal(fixture.Fill(reactions), fixture.Fill(pen));

        fixture.Shot("pen-off");
        fixture.Click(pen, MouseButton.Left);
        await fixture.Settle();
        Assert.True(fixture.ViewModel.Drawing.IsPenActive);
        Assert.True(pen.IsChecked);

        var overlay = fixture.Window.FindControl<Popup>("ReactionOverlay")!;
        var canvas = overlay.Child!.GetVisualDescendants().OfType<StrokeCanvas>().Single();
        var root = TopLevel.GetTopLevel(canvas)!;
        Assert.True(canvas.Bounds is { Width: > 100, Height: > 100 }, "Слой рисования накрывает всю область видео.");

        var start = canvas.TranslatePoint(new Point(40, 40), root)!.Value;
        var middle = canvas.TranslatePoint(new Point(120, 90), root)!.Value;
        var finish = canvas.TranslatePoint(new Point(200, 140), root)!.Value;
        root.MouseMove(start);
        root.MouseDown(start, MouseButton.Left);
        root.MouseMove(middle);
        await fixture.SettleFor(DrawingViewModel.SendInterval + TimeSpan.FromMilliseconds(80));
        root.MouseMove(finish);
        root.MouseUp(finish, MouseButton.Left);
        await fixture.Settle();

        await fixture.Sessions.Received(1).SendDrawAsync(Arg.Any<string>(), StrokePhase.Start, Arg.Any<IReadOnlyList<StrokePoint>>(), Arg.Any<CancellationToken>());
        await fixture.Sessions.Received(1).SendDrawAsync(Arg.Any<string>(), StrokePhase.End, Arg.Any<IReadOnlyList<StrokePoint>>(), Arg.Any<CancellationToken>());

        // The host relays the strokes back, including this device's own: that is what puts them on the picture.
        var mine = (string)fixture.Sessions.ReceivedCalls()
            .First(c => c.GetMethodInfo().Name == nameof(ISessionService.SendDrawAsync))
            .GetArguments()[0]!;
        fixture.Sessions.DrawReceived += Raise.Event<EventHandler<StrokeUpdate>>(
            fixture.Sessions,
            new StrokeUpdate(_host, mine, StrokePhase.Start, [new StrokePoint(0.2f, 0.2f), new StrokePoint(0.6f, 0.7f)], true));
        fixture.Sessions.DrawReceived += Raise.Event<EventHandler<StrokeUpdate>>(
            fixture.Sessions,
            new StrokeUpdate(_guest, "AB12", StrokePhase.Start, [new StrokePoint(0.1f, 0.8f), new StrokePoint(0.9f, 0.3f)], false));
        await fixture.Settle();

        Assert.Equal(2, fixture.ViewModel.Drawing.Strokes.Count);
        Assert.Equal(ParticipantColors.For(_guest), fixture.ViewModel.Drawing.Strokes[1].Color);
        Assert.NotEqual(fixture.ViewModel.Drawing.Strokes[0].Color, fixture.ViewModel.Drawing.Strokes[1].Color);

        var folder = Environment.GetEnvironmentVariable("GOLETHER_TEST_SCREENSHOTS");
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
            using var file = File.Create(Path.Combine(folder, "pen.png"));
            fixture.Window.CaptureRenderedFrame()!.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            if (root.CaptureRenderedFrame() is { } strokes)
            {
                using var strokeFile = File.Create(Path.Combine(folder, "strokes.png"));
                strokes.Save(strokeFile, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
        }

        // The button releases and the pen stops drawing.
        fixture.Click(pen, MouseButton.Left);
        await fixture.Settle();
        Assert.False(fixture.ViewModel.Drawing.IsPenActive);
        fixture.Sessions.ClearReceivedCalls();
        root.MouseDown(start, MouseButton.Left);
        root.MouseMove(middle);
        root.MouseUp(middle, MouseButton.Left);
        await fixture.Settle();
        await fixture.Sessions.DidNotReceiveWithAnyArgs().SendDrawAsync(default!, default, default!, TestContext.Current.CancellationToken);

        // The end of the session wipes the picture.
        fixture.ViewModel.Drawing.Reset();
        await fixture.Settle();
        Assert.Empty(fixture.ViewModel.Drawing.Strokes);
    });

    /// <summary>
    /// When the firewall would drop the participants, the window says so and offers to fix it; once the rule is
    /// there the warning goes away.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Firewall_WarnsAndOffersToAllow() => RunAsync(async fixture =>
    {
        var banner = fixture.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text == "Брандмауэр не пропускает участников");
        Assert.False(banner.IsEffectivelyVisible);

        fixture.Sessions.FirewallBlocked.Returns(true);
        fixture.ViewModel.Refresh();
        await fixture.Settle();
        Assert.True(banner.IsEffectivelyVisible);

        // Разрешение снимает предупреждение.
        fixture.Sessions.AllowFirewallAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            fixture.Sessions.FirewallBlocked.Returns(false);
            return Task.FromResult(true);
        });
        var allow = fixture.Window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Разрешить");
        Assert.True(allow.IsEffectivelyVisible);
        Assert.True(allow.IsEnabled);
        // Слой ручки не должен перехватывать щелчки, пока ручка отжата: иначе кнопка недостижима.
        var hit = fixture.Window.InputHitTest(fixture.PointOf(allow));
        Assert.True(
            hit is Visual visual && (visual == allow || visual.GetVisualAncestors().Contains(allow)),
            $"Щелчок по кнопке перехватывает {hit?.GetType().Name ?? "ничто"}.");
        fixture.Click(allow, MouseButton.Left);
        await fixture.Settle();

        await fixture.Sessions.Received(1).AllowFirewallAsync(Arg.Any<CancellationToken>());
        Assert.False(fixture.ViewModel.FirewallBlocked);
        Assert.False(banner.IsEffectivelyVisible);
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

        fixture.Conference.VideoQualityChanged += Raise.Event<EventHandler<VideoQualityChange>>(fixture.Conference, new VideoQualityChange(_guest, VideoQuality.Low));
        await fixture.Settle();
        Assert.True(Mark("Марина").IsEffectivelyVisible);
        Assert.False(Mark("Вы").IsEffectivelyVisible);

        fixture.Conference.VideoQualityChanged += Raise.Event<EventHandler<VideoQualityChange>>(fixture.Conference, new VideoQualityChange(_guest, VideoQuality.High));
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
        fixture.Conference.SpeakingChanged += Raise.Event<EventHandler<SpeakingChange>>(fixture.Conference, new SpeakingChange(_guest, true));
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
    /// The language selector lives in the settings window; it lists the languages with their flags, and picking one
    /// changes every text of the main window at once.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task LanguageSelector_SwitchesTheTextsOfTheWindow()
    {
        var localizer = Golether.Localization.Texts.Localizer;
        localizer.SetLanguage("ru");
        var settings = Substitute.For<ISettingsStore>();
        settings.GetAsync(LanguageService.SETTING, Arg.Any<CancellationToken>()).Returns("ru");
        var service = new LanguageService(localizer, settings);
        await service.InitializeAsync(CancellationToken.None);

        // The flags are images of the application resources, so the selector is built where the application runs.
        var selector = await _session.Value.Dispatch(() => new LanguageViewModel(service, Golether.UI.Localization.FlagImages.Load), TestContext.Current.CancellationToken);

        await RunAsync(
            async fixture =>
            {
                try
                {
                    // The selector is wired into the window as its language service; the settings window binds to it.
                    // Move to the start screen, where the hero text (the longest in the application) shows.
                    fixture.Sessions.GetSnapshot().Returns((SessionSnapshot?)null);
                    fixture.ViewModel.Refresh();
                    await fixture.Settle();
                    Assert.Equal(["Deutsch", "English", "Español", "Français", "Português", "Русский"], selector.Options.Select(o => o.Name));
                    Assert.All(selector.Options, o => Assert.NotNull(o.Flag));
                    Assert.Equal("Русский", selector.Selected!.Name);
                    Assert.Contains(fixture.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Смотрим вместе, где бы вы ни были");
                    SaveFrame(fixture.Window, "language-ru-start");

                    // Picking English, as the settings window's selector does, changes every text of the window at once.
                    selector.Selected = selector.Options.Single(o => o.Code == "en");
                    await fixture.Settle();
                    SaveFrame(fixture.Window, "language-en-start");

                    Assert.Equal("en", localizer.Current.Code);
                    Assert.Contains(fixture.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Watch together, wherever you are");
                    Assert.Contains(fixture.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Device A249-B9CC");
                    Assert.DoesNotContain(fixture.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Смотрим вместе, где бы вы ни были");
                    await settings.Received().SetAsync(LanguageService.SETTING, "en", Arg.Any<CancellationToken>());
                }
                finally
                {
                    localizer.SetLanguage("ru");
                }
            },
            selector);
    }

    /// <summary>
    /// While a session is being joined, a screen of its own says what is going on, shows that the application is
    /// busy and lets the user give up: first the way to the host is looked for, then the host is asked.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Joining_ShowsWhatIsGoingOnUntilTheHostLetsIn() => RunAsync(async fixture =>
    {
        // The user agreed to join (by a link or by the button) and the way to the host is being looked for.
        fixture.Sessions.GetSnapshot().Returns((SessionSnapshot?)null);
        var joining = new TaskCompletionSource();
        fixture.Sessions.JoinAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(joining.Task);
        fixture.ViewModel.Refresh();
        fixture.ViewModel.InviteLink = "golether://join/v1/test";
        var join = fixture.ViewModel.JoinCommand.ExecuteAsync(null);
        await fixture.Settle();

        var progress = fixture.Window.GetVisualDescendants().OfType<ProgressBar>().Where(bar => bar.IsEffectivelyVisible).ToArray();
        Assert.NotEmpty(progress);
        var shown = fixture.Window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToArray();
        Assert.Contains(fixture.ViewModel.JoinTitle, shown);
        Assert.Contains(Golether.Localization.Texts.Get("Main.Joining.Title"), shown);
        Assert.Contains(fixture.Window.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible && button.Command == fixture.ViewModel.CancelJoinCommand);
        SaveFrame(fixture.Window, "joining-connecting");

        // The host was reached: the code to read out appears on the same screen.
        fixture.ViewModel.IsWaitingForHost = true;
        fixture.ViewModel.VerificationText = "клён · река · камень";
        await fixture.Settle();
        shown = fixture.Window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToArray();
        Assert.Contains(Golether.Localization.Texts.Get("Main.Approval.Title"), shown);
        Assert.Contains("клён · река · камень", shown);
        SaveFrame(fixture.Window, "joining-approval");

        // Joined: the screen goes away.
        joining.SetResult();
        await join;
        await fixture.Settle();
        Assert.DoesNotContain(fixture.Window.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible && button.Command == fixture.ViewModel.CancelJoinCommand);
    });

    /// <summary>
    /// The start screen offers three ways to let guests from other networks in, and the field for a server address
    /// is there only for the way that needs it.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task StartScreen_ShowsTheServerFieldOnlyForAnOwnServer() => RunAsync(async fixture =>
    {
        fixture.Sessions.GetSnapshot().Returns((SessionSnapshot?)null);
        fixture.ViewModel.Refresh();
        await fixture.Settle();
        var field = fixture.Window.GetVisualDescendants().OfType<TextBox>().Single(box => box.PlaceholderText == "203.0.113.7:47900");
        var switches = fixture.Window.GetVisualDescendants().OfType<RadioButton>().Where(button => button.GroupName == "RendezvousMode").ToArray();

        Assert.Equal(3, switches.Length);
        Assert.True(switches[0].IsChecked);
        Assert.False(field.IsEffectivelyVisible);
        SaveFrame(fixture.Window, "start-rendezvous-automatic");

        switches[2].IsChecked = true;
        await fixture.Settle();

        Assert.Equal(RendezvousMode.Manual, fixture.ViewModel.RendezvousMode);
        Assert.False(switches[0].IsChecked);
        Assert.True(field.IsEffectivelyVisible);
        Assert.True(IsInside(field, fixture.Window));
        SaveFrame(fixture.Window, "start-rendezvous-manual");

        switches[1].IsChecked = true;
        await fixture.Settle();

        Assert.Equal(RendezvousMode.Direct, fixture.ViewModel.RendezvousMode);
        Assert.False(field.IsEffectivelyVisible);

        // The folded "Advanced" block is one more field of the card, not a bar of another kind: its header is as
        // tall as the text field above it and has the same rounded corners. The port waits inside until asked for.
        var name = fixture.Window.GetVisualDescendants().OfType<TextBox>().First(box => box.MaxLength == 64 && box.Width is double.NaN && box.IsEffectivelyVisible);
        var advanced = fixture.Window.GetVisualDescendants().OfType<Expander>().First(expander => expander.IsEffectivelyVisible);
        var header = advanced.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First();
        Assert.Equal(name.Bounds.Height, header.Bounds.Height, 1.0);
        Assert.Equal(name.CornerRadius, advanced.CornerRadius);
        Assert.DoesNotContain(fixture.Window.GetVisualDescendants().OfType<NumericUpDown>(), port => port.IsEffectivelyVisible);

        advanced.IsExpanded = true;
        await fixture.SettleFor(TimeSpan.FromMilliseconds(400));

        Assert.Contains(fixture.Window.GetVisualDescendants().OfType<NumericUpDown>(), port => port.IsEffectivelyVisible);
        SaveFrame(fixture.Window, "start-advanced");
    });

    /// <summary>
    /// The invitation window shows the link to click and the key to paste; the rarely needed extra address is folded
    /// away until asked for.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task InviteDialog_ShowsTheLinkAndTheKey() => RunAsync(async fixture =>
    {
        fixture.Sessions.CreateInvite(default!, default).ReturnsForAnyArgs(call => new Golether.Security.Invites.Invite
        {
            HostPeerId = _host,
            HostName = "Вы",
            SessionName = "Вечер кино",
            Endpoints = [Golether.Core.Networking.PeerEndpoint.Parse("192.168.1.2:47800")],
            Token = Golether.Security.Invites.Invite.CreateToken(),
            ExpiresAt = DateTimeOffset.UtcNow + call.Arg<TimeSpan>(),
        });
        var viewModel = new Golether.UI.ViewModels.Dialogs.InviteDialogViewModel(fixture.Sessions, Substitute.For<IDialogService>());
        viewModel.Regenerate();
        var dialog = new Golether.UI.Views.Dialogs.InviteDialog { DataContext = viewModel };
        dialog.Show();
        try
        {
            await fixture.Settle();
            var boxes = dialog.GetVisualDescendants().OfType<TextBox>().Where(box => box.IsEffectivelyVisible).ToArray();

            Assert.Equal(2, boxes.Length);
            Assert.StartsWith("https://", boxes[0].Text, StringComparison.Ordinal);
            Assert.StartsWith("golether://join/v1/", boxes[1].Text, StringComparison.Ordinal);
            Assert.All(dialog.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible), button => Assert.True(IsInside(button, dialog)));
            SaveFrame(dialog, "invite-dialog");

            dialog.GetVisualDescendants().OfType<Expander>().Single().IsExpanded = true;
            await fixture.Settle();

            Assert.Equal(3, dialog.GetVisualDescendants().OfType<TextBox>().Count(box => box.IsEffectivelyVisible));
            SaveFrame(dialog, "invite-dialog-advanced");
        }
        finally
        {
            dialog.Close();
        }
    });

    /// <summary>
    /// The buttons of the settings window sit in a bar under the scrolling part: they are in sight without scrolling
    /// to the end, however little room the window has.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task SettingsDialog_KeepsItsButtonsInSight() => RunAsync(async fixture =>
    {
        var viewModel = new Golether.UI.ViewModels.Dialogs.SettingsViewModel(
            language: null,
            devices: null,
            conferenceAvailable: false,
            conferenceNotice: "Камеры недоступны",
            canChooseAvatar: true,
            canConfigureTunnels: true,
            avatarPreview: null,
            pickAvatar: () => Task.FromResult<AvatarSelection?>(null),
            applyAvatar: _ => Task.CompletedTask,
            openTunnels: () => Task.CompletedTask,
            canChooseUpdates: true);
        var dialog = new Golether.UI.Views.Dialogs.SettingsDialog { DataContext = viewModel, SizeToContent = SizeToContent.Manual, Height = 360 };
        dialog.Show();
        try
        {
            await fixture.Settle();
            var scroll = dialog.GetVisualDescendants().OfType<ScrollViewer>().First();
            var buttons = dialog.GetVisualDescendants().OfType<Button>().Where(button => button.IsCancel || button.IsDefault).ToArray();

            // The settings do not fit into this height, so the list scrolls, and the buttons are not part of it.
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.Equal(2, buttons.Length);
            Assert.All(buttons, button => Assert.True(IsInside(button, dialog)));
            Assert.All(buttons, button => Assert.DoesNotContain(scroll, button.GetVisualAncestors()));
            SaveFrame(dialog, "settings-dialog-short");
        }
        finally
        {
            dialog.Close();
        }
    });

    /// <summary>
    /// Saves the rendered window for a visual check when <c>GOLETHER_TEST_SCREENSHOTS</c> names a folder.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="name">The file name without the extension.</param>
    private static void SaveFrame(Window window, string name)
    {
        var folder = Environment.GetEnvironmentVariable("GOLETHER_TEST_SCREENSHOTS");
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        using var file = File.Create(Path.Combine(folder, name + ".png"));
        window.CaptureRenderedFrame()!.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

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
    /// <param name="language">The language selector of the window, or <see langword="null"/> for a window without one.</param>
    /// <returns>A task that completes when the test is done and fails with the test's exception.</returns>
    private static Task RunAsync(Func<WindowFixture, Task> test, LanguageViewModel? language = null)
        => _session.Value.Dispatch(
            () =>
            {
                // The UI loop runs here until the test finishes, so its continuations execute and its failures (or a
                // hang) are reported instead of being lost with an unobserved task.
                var body = RunBodyAsync(test, language);
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
    /// <param name="language">The language selector of the window, or <see langword="null"/>.</param>
    /// <returns>A task that completes when the test is done.</returns>
    private static async Task RunBodyAsync(Func<WindowFixture, Task> test, LanguageViewModel? language)
    {
        var fixture = new WindowFixture(language);
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
        /// <param name="language">The language selector of the window, or <see langword="null"/>.</param>
        public WindowFixture(LanguageViewModel? language = null)
        {
            Sessions.GetSnapshot().Returns(_ => Snapshot());
            Settings.GetAsync(MainWindowViewModel.VOICE_VOLUME_SETTING_PREFIX + _guest.Value, Arg.Any<CancellationToken>()).Returns("70");
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
                Player,
                language: language);
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
        /// Gets or sets a value indicating whether the participant is in the session.
        /// </summary>
        public bool GuestPresent { get; set; } = true;

        /// <summary>
        /// Gets or sets how many more participants are in the session.
        /// </summary>
        public int ExtraGuests { get; set; }

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
        /// Returns the brush a button is filled with, as the style left it.
        /// </summary>
        /// <param name="control">The button.</param>
        /// <returns>The brush as text, or an empty string.</returns>
        public string Fill(Visual control)
            => control.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>()
                .FirstOrDefault(p => p.Name == "PART_ContentPresenter")?.Background?.ToString() ?? string.Empty;

        /// <summary>
        /// Saves a picture of the window when <c>GOLETHER_TEST_SCREENSHOTS</c> points at a folder.
        /// </summary>
        /// <param name="name">The file name without the extension.</param>
        public void Shot(string name)
        {
            var folder = Environment.GetEnvironmentVariable("GOLETHER_TEST_SCREENSHOTS");
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            Directory.CreateDirectory(folder);
            using var file = File.Create(Path.Combine(folder, $"{name}.png"));
            Window.CaptureRenderedFrame()!.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

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
                HostPeerId = _host,
                Participants =
                [
                    new ParticipantView(new ParticipantInfo(_host, "Вы", true), new ParticipantStatus { PeerId = _host, Position = Position }, true),
                    .. GuestPresent
                        ? [new ParticipantView(new ParticipantInfo(_guest, "Марина", false), new ParticipantStatus { PeerId = _guest, MicrophoneOff = GuestMicrophoneOff }, false)]
                        : Array.Empty<ParticipantView>(),
                    .. Enumerable.Range(0, ExtraGuests).Select(i =>
                    {
                        var peer = PeerId.Parse(new string((char)('d' + i), 64));
                        return new ParticipantView(new ParticipantInfo(peer, $"Гость {i + 1}", false), new ParticipantStatus { PeerId = peer }, false);
                    }),
                ],
                Playback = PlaybackState.Initial(_host, 0) with
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
