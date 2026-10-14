using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Golether.Localization;
using Golether.Web.Server.Contracts;
using Golether.Web.Server.UI.Services;
using Golether.Web.Server.UI.ViewModels;
using Golether.Web.Server.UI.Views;
using NSubstitute;

namespace Golether.Web.Server.UI.Tests;

/// <summary>
/// The window of the monitor in the Avalonia headless platform: it shows the state, the table lines up with its
/// header, and a narrow window still holds everything.
/// </summary>
[Collection(TextsCollection.Name)]
public sealed class MainWindowTests : IDisposable
{
    /// <summary>
    /// The headless session, shared by the tests of this class.
    /// </summary>
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(() => HeadlessUnitTestSession.StartNew(typeof(TestApp)));

    /// <summary>
    /// The settings file of the test.
    /// </summary>
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"golether-monitor-{Guid.NewGuid():N}.json");

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindowTests"/> class.
    /// </summary>
    public MainWindowTests() => File.WriteAllText(_file, """{ "Rendezvous": { "Port": 47900 }, "Urls": "http://127.0.0.1:47980" }""");

    /// <summary>
    /// The window shows the state and the events; every row of the table lines up with the header, and the columns
    /// follow the header when its divider is moved.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Window_ShowsTheJournalInColumns() => Session.Value.Dispatch(
        async () =>
        {
            var model = CreateModel();
            var window = new MainWindow { DataContext = model, Width = 1100, Height = 720 };
            window.Show();
            await model.RefreshAsync(TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            SaveFrame(window, "monitor-journal");

            var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToArray();
            Assert.Contains(Texts.Get("Monitor.Status.Running"), texts);
            Assert.Contains("203.0.113.7:51234", texts);
            Assert.Contains(Texts.Get("Monitor.Event.HostRegistered"), texts);

            var header = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == Texts.Get("Monitor.Column.Event"));
            var cell = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == Texts.Get("Monitor.Event.HostRegistered"));
            Assert.Equal(header.TranslatePoint(default, window)!.Value.X, cell.TranslatePoint(default, window)!.Value.X, 1.0);

            model.Columns.Address = new GridLength(3, GridUnitType.Star);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(header.TranslatePoint(default, window)!.Value.X, cell.TranslatePoint(default, window)!.Value.X, 1.0);

            // A narrow window: the buttons wrap instead of leaving the window, and every column stays visible.
            window.Width = 660;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            SaveFrame(window, "monitor-narrow");
            foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
            {
                var left = button.TranslatePoint(default, window)!.Value.X;
                Assert.InRange(left + button.Bounds.Width, 0, window.Bounds.Width + 0.5);
            }

            var details = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == Texts.Get("Monitor.Column.Details"));
            Assert.InRange(details.TranslatePoint(default, window)!.Value.X, 0, window.Bounds.Width - 40);

            window.Close();
        },
        TestContext.Current.CancellationToken);

    /// <summary>
    /// The question before stopping: the question, what stopping does, and a cancel button that is the default
    /// answer next to the red one that stops.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task StopQuestion_DefaultsToCancel() => Session.Value.Dispatch(
        () =>
        {
            var dialog = new ConfirmDialog(Texts.Get("Monitor.Stop.Confirm.Title"), Texts.Get("Monitor.Stop.Confirm.Text"), Texts.Get("Monitor.Stop"));
            dialog.Show();
            Dispatcher.UIThread.RunJobs();
            dialog.UpdateLayout();
            SaveFrame(dialog, "monitor-stop-question");

            var buttons = dialog.GetVisualDescendants().OfType<Button>().ToArray();
            var cancel = Assert.Single(buttons, b => b.IsDefault);
            Assert.True(cancel.IsCancel);
            Assert.Equal(Texts.Get("Monitor.Cancel"), cancel.Content);
            Assert.Contains(buttons, b => b.Classes.Contains("danger") && Equals(b.Content, Texts.Get("Monitor.Stop")));
            dialog.Close();
        },
        TestContext.Current.CancellationToken);

    /// <inheritdoc />
    public void Dispose() => File.Delete(_file);

    /// <summary>
    /// Creates the model over stubs: a running service with two events.
    /// </summary>
    /// <returns>The model.</returns>
    private MainViewModel CreateModel()
    {
        var api = Substitute.For<IServerApi>();
        var service = Substitute.For<IServiceManager>();
        service.QueryAsync(Arg.Any<CancellationToken>()).Returns(ServiceState.Running);
        api.GetHealthAsync(Arg.Any<CancellationToken>()).Returns(new HealthReport("ok", "0.3.1", 1, 7384, 2, 5, 10, 9, 1, 40));
        var started = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.FromHours(3));
        api.GetJournalAsync(0, Arg.Any<CancellationToken>()).Returns(new JournalPage(
            started,
            [
                new JournalEntry(1, started, JournalEvent.ServerStarted, Detail: "0.3.1"),
                new JournalEntry(2, started.AddSeconds(5), JournalEvent.HostRegistered, "203.0.113.7:51234", "EC80-995D-ECE8-183B", "1a2b3c4d"),
                new JournalEntry(3, started.AddSeconds(9), JournalEvent.MeetingFailed, "198.51.100.20:40000", Room: "1a2b3c4d", Reason: Golether.Transports.Rendezvous.Protocol.RendezvousError.Timeout),
            ],
            3));
        return new MainViewModel(api, service, new ServerSettingsStore(_file, service), Substitute.For<IDialogs>(), TimeProvider.System);
    }

    /// <summary>
    /// Saves the rendered window when <c>GOLETHER_TEST_SCREENSHOTS</c> names a folder.
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
}

/// <summary>
/// The monitor application on the headless platform, drawn with Skia so frames can be saved.
/// </summary>
public static class TestApp
{
    /// <summary>
    /// Builds the application.
    /// </summary>
    /// <returns>The builder.</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
