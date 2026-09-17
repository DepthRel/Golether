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
    /// A right click on a participant tile opens the menu with the two switch-off commands; the host's own tile has
    /// no menu; the menu command asks the session to switch the microphone off.
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
        var items = menu.Items.OfType<MenuItem>().ToArray();
        Assert.Equal(["Выключить микрофон", "Выключить камеру"], items.Select(i => (string)i.Header!));
        Assert.DoesNotContain(items, i => ((string)i.Header!).Contains("Включить", StringComparison.Ordinal));

        items[0].Command!.Execute(null);
        await fixture.Settle();
        await fixture.Sessions.Received(1).SwitchOffParticipantDevicesAsync(Guest, true, false);
        menu.Close();

        // A microphone that is already off cannot be switched off again.
        fixture.GuestMicrophoneOff = true;
        fixture.ViewModel.Refresh();
        await fixture.Settle();
        Assert.False(items[0].Command!.CanExecute(null));
    });

    /// <summary>
    /// A click on the event feed hides it; the header shows it again.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task EventFeed_FoldsAndUnfolds() => RunAsync(async fixture =>
    {
        var feed = fixture.Window.FindControl<ScrollViewer>("EventFeed")!;
        Assert.True(feed.IsVisible);

        fixture.Click(feed, MouseButton.Left);
        await fixture.Settle();
        Assert.False(feed.IsVisible);
        Assert.False(fixture.ViewModel.EventsExpanded);

        var header = fixture.Window.GetVisualDescendants().OfType<Button>().Single(b => AutomationName(b) == "Лента событий");
        fixture.Click(header, MouseButton.Left);
        await fixture.Settle();
        Assert.True(feed.IsVisible);
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

        var items = ((MenuFlyout)more.Flyout!).Items.OfType<MenuItem>().ToArray();
        Assert.Equal(["Туннели AWG", "Пригласить", "Завершить"], items.Select(i => (string)i.Header!));
        Assert.Same(fixture.ViewModel.LeaveCommand, items[2].Command);

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
    /// Returns the automation name of a control.
    /// </summary>
    /// <param name="control">The control.</param>
    /// <returns>The name.</returns>
    private static string? AutomationName(Control control) => Avalonia.Automation.AutomationProperties.GetName(control);

    /// <summary>
    /// Runs a test on the UI thread with a fresh window.
    /// </summary>
    /// <param name="test">The test.</param>
    /// <returns>A task that completes when the test is done.</returns>
    private static Task RunAsync(Func<WindowFixture, Task> test)
        => Session.Value.Dispatch(
            async () =>
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
            },
            TestContext.Current.CancellationToken);

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
                Substitute.For<ISettingsStore>(),
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
            var player = new PlayerSnapshot(true, TimeSpan.FromMinutes(1), TimeSpan.FromHours(2), true, false, TimeSpan.FromSeconds(20), 1.0);
            return new SessionSnapshot
            {
                IsHost = true,
                State = SessionState.Active,
                SessionName = "Вечер кино",
                HostPeerId = Host,
                Participants =
                [
                    new ParticipantView(new ParticipantInfo(Host, "Вы", true), new ParticipantStatus { PeerId = Host }, true),
                    new ParticipantView(new ParticipantInfo(Guest, "Марина", false), new ParticipantStatus { PeerId = Guest, MicrophoneOff = GuestMicrophoneOff }, false),
                ],
                Playback = PlaybackState.Initial(Host, 0) with { State = PlayState.Paused, Position = TimeSpan.FromMinutes(1) },
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
