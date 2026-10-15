using NSubstitute;
using Golether.Core.Enums;
using Golether.Localization;
using Golether.Web.Server.Contracts;
using Golether.Web.Server.UI.Enums;
using Golether.Web.Server.UI.Services;
using Golether.Web.Server.UI.ViewModels;

namespace Golether.Web.Server.UI.Tests;

/// <summary>
/// Tests of the main window model over stubs of the server, the service and the dialogs.
/// </summary>
[Collection(TextsCollection.NAME)]
public sealed class MainViewModelTests : IDisposable
{
    /// <summary>
    /// The settings file of the test.
    /// </summary>
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"golether-monitor-{Guid.NewGuid():N}.json");

    /// <summary>
    /// The server.
    /// </summary>
    private readonly IServerApi _api = Substitute.For<IServerApi>();

    /// <summary>
    /// The service.
    /// </summary>
    private readonly IServiceManager _service = Substitute.For<IServiceManager>();

    /// <summary>
    /// The dialogs.
    /// </summary>
    private readonly IDialogs _dialogs = Substitute.For<IDialogs>();

    /// <summary>
    /// When the run of the server started.
    /// </summary>
    private readonly DateTimeOffset _started = new(2026, 10, 14, 8, 0, 0, TimeSpan.FromHours(3));

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModelTests"/> class.
    /// </summary>
    public MainViewModelTests() => File.WriteAllText(_file, """{ "Rendezvous": { "Port": 47900 } }""");

    /// <summary>
    /// The state of the service and the answers of the server make one status: a service that runs and answers is
    /// good, a server that answers without a service runs by hand, a stopped one is bad.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Status_PutsTheServiceAndTheServerTogether()
    {
        var model = CreateModel();
        _service.QueryAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(ServiceState.Running);
        _api.GetHealthAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Health(3725));

        await model.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServerCondition.Good, model.Condition);
        Assert.Equal(Texts.Get("Monitor.Status.Running"), model.StatusText);
        Assert.Equal("01:02:05", model.UptimeText);
        Assert.False(model.StartCommand.CanExecute(null));
        Assert.True(model.StopCommand.CanExecute(null));

        model = CreateModel();
        _service.QueryAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(ServiceState.Unknown);
        await model.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Texts.Get("Monitor.Status.RunningServiceUnknown"), model.StatusText);

        model = CreateModel();
        _service.QueryAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(ServiceState.NotInstalled);
        await model.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServerCondition.Warning, model.Condition);
        Assert.Equal(Texts.Get("Monitor.Status.RunningManually"), model.StatusText);
        Assert.False(model.StopCommand.CanExecute(null));

        model = CreateModel();
        _service.QueryAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(ServiceState.Stopped);
        _api.GetHealthAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs((HealthReport?)null);
        await model.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServerCondition.Bad, model.Condition);
        Assert.Equal("—", model.UptimeText);
        Assert.True(model.StartCommand.CanExecute(null));
    }

    /// <summary>
    /// New events appear on top, in the words of the user; the search keeps the matching rows, new ones included; a
    /// restarted server is read from its start again without losing what was received.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Journal_PutsNewEventsOnTopAndSearches()
    {
        var model = CreateModel();
        _service.QueryAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(ServiceState.Running);
        _api.GetHealthAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Health(10));
        _api.GetJournalAsync(0, Arg.Any<CancellationToken>()).Returns(Page(_started, Entry(1, JournalEvent.ServerStarted), Entry(2, JournalEvent.HostRegistered, "203.0.113.7:51234")));
        await model.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal([2L, 1L], model.Rows.Select(r => r.Entry.Sequence));
        Assert.Equal(Texts.Get("Monitor.Event.HostRegistered"), model.Rows[0].Event);
        Assert.Equal("203.0.113.7:51234", model.Rows[0].Address);

        model.SearchText = "203.0.113";
        Assert.Single(model.Rows);
        _api.GetJournalAsync(2, Arg.Any<CancellationToken>()).Returns(Page(_started, Entry(3, JournalEvent.MeetingFailed, "203.0.113.9:4000", RendezvousError.RoomNotFound), Entry(4, JournalEvent.HostLeft, "198.51.100.1:1")));
        await model.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal([3L, 2L], model.Rows.Select(r => r.Entry.Sequence));
        Assert.Contains(Texts.Get("Monitor.Reason.RoomNotFound"), model.Rows[0].Details, StringComparison.Ordinal);
        Assert.True(model.Rows[0].IsProblem);

        model.SearchText = string.Empty;
        _api.GetJournalAsync(4, Arg.Any<CancellationToken>()).Returns(Page(_started.AddMinutes(5), Entry(1, JournalEvent.ServerStarted)));
        _api.GetJournalAsync(1, Arg.Any<CancellationToken>()).Returns(Page(_started.AddMinutes(5), Entry(1, JournalEvent.ServerStarted), Entry(2, JournalEvent.Listening)));
        await model.RefreshAsync(TestContext.Current.CancellationToken);
        await model.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(6, model.Rows.Count);
        Assert.Equal(JournalEvent.Listening, model.Rows[0].Entry.Event);
    }

    /// <summary>
    /// The export writes every event received, the oldest first, with the time zone; nothing is written unless asked.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Export_WritesTheJournalOldestFirst()
    {
        var model = CreateModel();
        _service.QueryAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(ServiceState.Running);
        _api.GetHealthAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Health(10));
        _api.GetJournalAsync(0, Arg.Any<CancellationToken>()).Returns(Page(_started, Entry(1, JournalEvent.ServerStarted), Entry(2, JournalEvent.HostRegistered, "203.0.113.7:51234")));
        Assert.False(model.ExportCommand.CanExecute(null));
        await model.RefreshAsync(TestContext.Current.CancellationToken);
        var written = new KeepingStream();
        _dialogs.SaveFileAsync(Arg.Any<string>()).Returns(written);

        await model.ExportCommand.ExecuteAsync(null);

        var lines = System.Text.Encoding.UTF8.GetString(written.Kept).TrimStart('﻿').Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
        Assert.Contains(Texts.Get("Monitor.Event.ServerStarted"), lines[2], StringComparison.Ordinal);
        Assert.Contains("203.0.113.7:51234", lines[3], StringComparison.Ordinal);
        Assert.Contains("+03:00", lines[3], StringComparison.Ordinal);
        Assert.Equal(Texts.Format("Monitor.Done.Exported", 2), model.Message);
    }

    /// <summary>
    /// The actions on the service tell how they went: done, declined by the user, or failed with the evidence.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Actions_TellHowTheyWent()
    {
        var model = CreateModel();
        _service.QueryAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(ServiceState.Stopped);
        await model.RefreshAsync(TestContext.Current.CancellationToken);

        await model.StartCommand.ExecuteAsync(null);
        Assert.Equal(Texts.Get("Monitor.Done.Started"), model.Message);

        _service.StartAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Task.FromException(new ElevationCancelledException()));
        await model.StartCommand.ExecuteAsync(null);
        Assert.Equal(Texts.Get("Monitor.Error.Declined"), model.Message);

        _service.StartAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("Unit not found")));
        await model.StartCommand.ExecuteAsync(null);
        Assert.Equal(Texts.Format("Monitor.Error.Failed", "Unit not found"), model.Message);
        Assert.False(model.IsBusy);
    }

    /// <summary>
    /// Stopping the server asks first; "Cancel" leaves the service alone, only a confirmed stop reaches the system.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Stop_AsksFirst()
    {
        var model = CreateModel();
        _service.QueryAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(ServiceState.Running);
        await model.RefreshAsync(TestContext.Current.CancellationToken);

        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        await model.StopCommand.ExecuteAsync(null);
        await _service.DidNotReceiveWithAnyArgs().StopAsync(Arg.Any<CancellationToken>());
        Assert.Empty(model.Message);

        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        await model.StopCommand.ExecuteAsync(null);
        await _service.ReceivedWithAnyArgs(1).StopAsync(Arg.Any<CancellationToken>());
        await _dialogs.Received().ConfirmAsync(Texts.Get("Monitor.Stop.Confirm.Title"), Texts.Get("Monitor.Stop.Confirm.Text"), Texts.Get("Monitor.Stop"));
        Assert.Equal(Texts.Get("Monitor.Done.Stopped"), model.Message);
    }

    /// <summary>
    /// The language changes at once: the state, the rows already in the table and the texts of the window speak the
    /// new language, and the choice is remembered for the next start. Nothing is restarted.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Language_ChangesAtOnceAndIsRemembered()
    {
        var preferencesFile = Path.Combine(Path.GetTempPath(), $"golether-monitor-{Guid.NewGuid():N}", "server-monitor.json");
        var model = new MainViewModel(_api, _service, new ServerSettingsStore(_file, _service), _dialogs, TimeProvider.System, new MonitorPreferences(preferencesFile));
        _service.QueryAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(ServiceState.Running);
        _api.GetHealthAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Health(10));
        _api.GetJournalAsync(0, Arg.Any<CancellationToken>()).Returns(Page(_started, Entry(1, JournalEvent.ServerStarting)));
        await model.RefreshAsync(TestContext.Current.CancellationToken);
        var english = model.Languages.Single(l => l.Code == "en");
        try
        {
            Assert.Equal("ru", model.SelectedLanguage.Code);

            // The language changes on the thread of the windows, as the settings of the monitor change it.
            await HeadlessSession.Instance.Dispatch(() => model.SelectedLanguage = english, TestContext.Current.CancellationToken);

            Assert.Equal("Running as a service", model.StatusText);
            Assert.Equal("The server is starting", model.Rows[0].Event);
            Assert.Equal("Running as a service", Golether.Web.Server.UI.Localization.LocalizationSource.Instance["Monitor.Status.Running"]);
            Assert.Equal("en", new MonitorPreferences(preferencesFile).LoadLanguage());
            await _service.DidNotReceiveWithAnyArgs().ReplaceSettingsAndRestartAsync(default!, default!, Arg.Any<CancellationToken>());
        }
        finally
        {
            await HeadlessSession.Instance.Dispatch(() => Texts.Localizer.SetLanguage("ru"), CancellationToken.None);
            Directory.Delete(Path.GetDirectoryName(preferencesFile)!, recursive: true);
        }
    }

    /// <summary>
    /// Every event, every reason of a refusal and every state of the status has words in every language.
    /// </summary>
    /// <param name="code">The language.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void EveryValue_HasWordsInEveryLanguage(string code)
    {
        var pack = LanguageCatalog.LoadEmbedded().Find(code)!;
        var keys = Enum.GetValues<JournalEvent>().Select(v => "Monitor.Event." + v)
            .Concat(Enum.GetValues<RendezvousError>().Select(v => "Monitor.Reason." + v))
            .Concat(new[] { "Running", "RunningServiceUnknown", "NotAnswering", "Starting", "Stopping", "RunningManually", "Stopped", "NotInstalled", "Unknown" }.Select(v => "Monitor.Status." + v));

        Assert.DoesNotContain(keys, key => !pack.TryGet(key, out _));
    }

    /// <inheritdoc />
    public void Dispose() => File.Delete(_file);

    /// <summary>
    /// Creates the model over the stubs.
    /// </summary>
    /// <returns>The model.</returns>
    private MainViewModel CreateModel()
        => new(_api, _service, new ServerSettingsStore(_file, _service), _dialogs, TimeProvider.System);

    /// <summary>
    /// Builds a health report.
    /// </summary>
    /// <param name="uptime">The uptime in seconds.</param>
    /// <returns>The report.</returns>
    private static HealthReport Health(long uptime) => new("ok", "0.3.1", 1, uptime, 2, 5, 10, 9, 0, 40);

    /// <summary>
    /// Builds an event.
    /// </summary>
    /// <param name="sequence">The number.</param>
    /// <param name="kind">What happened.</param>
    /// <param name="address">The address.</param>
    /// <param name="reason">The reason.</param>
    /// <returns>The event.</returns>
    private JournalEntry Entry(long sequence, JournalEvent kind, string? address = null, RendezvousError? reason = null)
        => new(sequence, _started.AddSeconds(sequence), kind, address, Reason: reason);

    /// <summary>
    /// Builds an answer of the journal.
    /// </summary>
    /// <param name="started">When the run started.</param>
    /// <param name="entries">The events.</param>
    /// <returns>The answer.</returns>
    private static JournalPage Page(DateTimeOffset started, params JournalEntry[] entries)
        => new(started, entries, entries.Length == 0 ? 0 : entries[^1].Sequence);

    /// <summary>
    /// A memory stream that keeps its bytes after it is closed.
    /// </summary>
    private sealed class KeepingStream : MemoryStream
    {
        /// <summary>
        /// Gets the bytes written before it was closed.
        /// </summary>
        public byte[] Kept { get; private set; } = [];

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            Kept = ToArray();
            base.Dispose(disposing);
        }
    }
}
