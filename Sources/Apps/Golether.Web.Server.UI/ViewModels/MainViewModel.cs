using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Localization;
using Golether.Web.Server.Contracts;
using Golether.Web.Server.UI.Services;

namespace Golether.Web.Server.UI.ViewModels;

/// <summary>
/// How the server is doing, in one word for the colour of the status.
/// </summary>
public enum ServerCondition
{
    /// <summary>
    /// Not known yet.
    /// </summary>
    Unknown,

    /// <summary>
    /// Running and answering.
    /// </summary>
    Good,

    /// <summary>
    /// Changing its state, or running without being a service.
    /// </summary>
    Warning,

    /// <summary>
    /// Not running, or running without answering.
    /// </summary>
    Bad,
}

/// <summary>
/// The main window of the monitor: the state of the server and its service, the journal of events and the settings.
/// <see cref="RefreshAsync"/> asks everything again; the window calls it every second.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>
    /// The largest number of events the monitor holds; the oldest are dropped. The server itself keeps fewer.
    /// </summary>
    public const int MaxRows = 20000;

    /// <summary>
    /// How often the state of the service is asked for, in refreshes: running a tool every second is not needed.
    /// </summary>
    private const int ServiceQueryEvery = 2;

    /// <summary>
    /// Reads the server.
    /// </summary>
    private readonly IServerApi _api;

    /// <summary>
    /// Controls the service.
    /// </summary>
    private readonly IServiceManager _service;

    /// <summary>
    /// Reads and writes the settings.
    /// </summary>
    private readonly ServerSettingsStore _settings;

    /// <summary>
    /// The questions to the user.
    /// </summary>
    private readonly IDialogs _dialogs;

    /// <summary>
    /// The time provider.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Every event received, the newest first.
    /// </summary>
    private readonly List<JournalRowViewModel> _rows = [];

    /// <summary>
    /// Keeps one refresh at a time.
    /// </summary>
    private readonly SemaphoreSlim _refreshing = new(1, 1);

    /// <summary>
    /// The number of the last event received.
    /// </summary>
    private long _after;

    /// <summary>
    /// When the run of the server the events belong to started.
    /// </summary>
    private DateTimeOffset? _serverStarted;

    /// <summary>
    /// The number of refreshes so far.
    /// </summary>
    private long _refreshes;

    /// <summary>
    /// Keeps the chosen language, or <see langword="null"/> when nothing is kept (tests).
    /// </summary>
    private readonly MonitorPreferences? _preferences;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class.
    /// </summary>
    /// <param name="api">Reads the server.</param>
    /// <param name="service">Controls the service.</param>
    /// <param name="settings">Reads and writes the settings.</param>
    /// <param name="dialogs">The questions to the user.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="preferences">Keeps the chosen language, or <see langword="null"/> to keep nothing.</param>
    public MainViewModel(IServerApi api, IServiceManager service, ServerSettingsStore settings, IDialogs dialogs, TimeProvider timeProvider, MonitorPreferences? preferences = null)
    {
        _preferences = preferences;
        SelectedLanguage = Texts.Localizer.Current;
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.IsChanged))
            {
                SaveSettingsCommand.NotifyCanExecuteChanged();
                RevertSettingsCommand.NotifyCanExecuteChanged();
            }
        };
        LoadSettings();
    }

    /// <summary>
    /// Gets the languages the monitor speaks.
    /// </summary>
    public IReadOnlyList<LanguageInfo> Languages => Texts.Localizer.Languages;

    /// <summary>
    /// Gets or sets the language of the monitor. A change applies at once, to the window and to the journal already
    /// shown, and is remembered for the next start; the server is not touched.
    /// </summary>
    [ObservableProperty]
    public partial LanguageInfo SelectedLanguage { get; set; }

    /// <summary>
    /// Gets the widths of the columns of the journal.
    /// </summary>
    public JournalColumns Columns { get; } = new();

    /// <summary>
    /// Gets the rows shown: the events that match <see cref="SearchText"/>, the newest first.
    /// </summary>
    public ObservableCollection<JournalRowViewModel> Rows { get; } = [];

    /// <summary>
    /// Gets the settings form.
    /// </summary>
    public SettingsViewModel Settings { get; } = new();

    /// <summary>
    /// Gets the settings file, shown under the form.
    /// </summary>
    public string SettingsFile => _settings.Path;

    /// <summary>
    /// Gets or sets the text the rows are searched for.
    /// </summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the state of the service.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(Condition))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))]
    public partial ServiceState ServiceState { get; set; }

    /// <summary>
    /// Gets or sets the last health report, or <see langword="null"/> when the server does not answer.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(Condition), nameof(UptimeText), nameof(RoomsText), nameof(ConnectionsText), nameof(MeetingsText), nameof(RefusedText), nameof(VersionText))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))]
    public partial HealthReport? Health { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an action on the service runs.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand), nameof(SaveSettingsCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// Gets or sets the result of the last action, or an empty text.
    /// </summary>
    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets the state of the server in words.
    /// </summary>
    public string StatusText => Texts.Get("Monitor.Status." + Describe().Key);

    /// <summary>
    /// Gets how the server is doing, for the colour of the status.
    /// </summary>
    public ServerCondition Condition => Describe().Condition;

    /// <summary>
    /// Gets how long the server has been running.
    /// </summary>
    public string UptimeText => Health is { } health ? FormatUptime(TimeSpan.FromSeconds(health.UptimeSeconds)) : "—";

    /// <summary>
    /// Gets the rooms held now.
    /// </summary>
    public string RoomsText => Health?.Rooms.ToString(CultureInfo.CurrentCulture) ?? "—";

    /// <summary>
    /// Gets the connections open now.
    /// </summary>
    public string ConnectionsText => Health?.Connections.ToString(CultureInfo.CurrentCulture) ?? "—";

    /// <summary>
    /// Gets the meetings: introduced of asked for.
    /// </summary>
    public string MeetingsText => Health is { } health ? $"{health.MeetingsIntroduced} / {health.MeetingsRequested}" : "—";

    /// <summary>
    /// Gets the refusals for exceeding a limit.
    /// </summary>
    public string RefusedText => Health?.Refused.ToString(CultureInfo.CurrentCulture) ?? "—";

    /// <summary>
    /// Gets the version of the running server.
    /// </summary>
    public string VersionText => Health?.Version is { } version ? version.Split('+')[0] : "—";

    /// <summary>
    /// Asks the server and the system for everything again and adds the new events on top.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the view is up to date.</returns>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshing.WaitAsync(0, cancellationToken).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            if (_refreshes++ % ServiceQueryEvery == 0)
            {
                ServiceState = await _service.QueryAsync(cancellationToken).ConfigureAwait(true);
            }

            Health = await _api.GetHealthAsync(cancellationToken).ConfigureAwait(true);
            if (Health is not null && await _api.GetJournalAsync(_after, cancellationToken).ConfigureAwait(true) is { } page)
            {
                Take(page);
            }
        }
        finally
        {
            _refreshing.Release();
        }
    }

    /// <summary>
    /// Adds the events of an answer of the server.
    /// </summary>
    /// <param name="page">The answer.</param>
    private void Take(JournalPage page)
    {
        if (_serverStarted is { } started && started != page.Started)
        {
            // The server was restarted: its numbering began again. What was received stays in the table.
            _after = 0;
        }

        _serverStarted = page.Started;
        var fresh = page.Entries.Where(e => e.Sequence > _after).ToList();
        if (fresh.Count == 0)
        {
            return;
        }

        _after = fresh[^1].Sequence;
        var search = SearchText.Trim();
        foreach (var entry in fresh)
        {
            var row = new JournalRowViewModel(entry, Columns);
            _rows.Insert(0, row);
            if (row.Matches(search))
            {
                Rows.Insert(0, row);
            }
        }

        if (_rows.Count > MaxRows)
        {
            var dropped = _rows.GetRange(MaxRows, _rows.Count - MaxRows);
            _rows.RemoveRange(MaxRows, _rows.Count - MaxRows);
            foreach (var row in dropped)
            {
                Rows.Remove(row);
            }
        }

        ExportCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Switches the language of the monitor.
    /// </summary>
    /// <param name="value">The chosen language.</param>
    partial void OnSelectedLanguageChanged(LanguageInfo value)
    {
        if (value is null || value.Code == Texts.Localizer.Current.Code)
        {
            return;
        }

        Texts.Localizer.SetLanguage(value.Code);
        var kept = _preferences?.SaveLanguage(value.Code) ?? true;

        // The texts made by the model follow: the state, the time, and every row of the journal.
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(UptimeText));
        var rows = _rows.Select(row => new JournalRowViewModel(row.Entry, Columns)).ToList();
        _rows.Clear();
        _rows.AddRange(rows);
        OnSearchTextChanged(SearchText);
        Message = kept ? string.Empty : Texts.Format("Monitor.Language.NotKept", _preferences!.Path);
    }

    /// <summary>
    /// Shows the rows that match the new search.
    /// </summary>
    /// <param name="value">The search.</param>
    partial void OnSearchTextChanged(string value)
    {
        var search = value.Trim();
        Rows.Clear();
        foreach (var row in _rows.Where(r => r.Matches(search)))
        {
            Rows.Add(row);
        }
    }

    /// <summary>
    /// Starts the service.
    /// </summary>
    /// <returns>A task that completes when the system answered.</returns>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartAsync() => RunAsync(_service.StartAsync, "Monitor.Done.Started");

    /// <summary>
    /// Stops the service, after the user confirmed it: the hosts lose the server until it is started again.
    /// </summary>
    /// <returns>A task that completes when the system answered or the user changed their mind.</returns>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        if (!await _dialogs.ConfirmAsync(Texts.Get("Monitor.Stop.Confirm.Title"), Texts.Get("Monitor.Stop.Confirm.Text"), Texts.Get("Monitor.Stop")))
        {
            return;
        }

        await RunAsync(_service.StopAsync, "Monitor.Done.Stopped");
    }

    /// <summary>
    /// Tells whether the service can be started: it exists and does not run.
    /// </summary>
    /// <returns><see langword="true"/> when it can.</returns>
    private bool CanStart() => !IsBusy && ServiceState is ServiceState.Stopped;

    /// <summary>
    /// Tells whether the service can be stopped: it runs or is starting.
    /// </summary>
    /// <returns><see langword="true"/> when it can.</returns>
    private bool CanStop() => !IsBusy && ServiceState is ServiceState.Running or ServiceState.Starting;

    /// <summary>
    /// Saves the journal held by the monitor to a file chosen by the user, the oldest event first. Nothing is saved
    /// otherwise: this is the only way the events reach a disk.
    /// </summary>
    /// <returns>A task that completes when the file is written or the user cancelled.</returns>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        var now = _timeProvider.GetLocalNow();
        await using var stream = await _dialogs.SaveFileAsync($"golether-server-{now:yyyy-MM-dd-HHmm}.log");
        if (stream is null)
        {
            return;
        }

        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await writer.WriteLineAsync(Texts.Format("Monitor.Export.Header", VersionText, now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)));
        await writer.WriteLineAsync(string.Join(" | ", Texts.Get("Monitor.Column.Time"), Texts.Get("Monitor.Column.Address"), Texts.Get("Monitor.Column.Event"), Texts.Get("Monitor.Column.Details")));
        for (var i = _rows.Count - 1; i >= 0; i--)
        {
            await writer.WriteLineAsync(_rows[i].ToLine());
        }

        Message = Texts.Format("Monitor.Done.Exported", _rows.Count);
    }

    /// <summary>
    /// Tells whether there is anything to export.
    /// </summary>
    /// <returns><see langword="true"/> when there is.</returns>
    private bool CanExport() => _rows.Count > 0;

    /// <summary>
    /// Checks the settings, writes them and restarts the service.
    /// </summary>
    /// <returns>A task that completes when the settings are in place or the user cancelled.</returns>
    [RelayCommand(CanExecute = nameof(CanSaveSettings))]
    private async Task SaveSettingsAsync()
    {
        if (!Settings.TryValidate(out var settings))
        {
            Message = Texts.Get("Monitor.Settings.Invalid");
            return;
        }

        var installed = ServiceState is not (ServiceState.NotInstalled or ServiceState.Unknown);
        await RunAsync(
            token => _settings.SaveAsync(settings, installed, token),
            installed ? "Monitor.Done.SavedRestarted" : "Monitor.Done.SavedRestartYourself");
        LoadSettings();
    }

    /// <summary>
    /// Tells whether the settings can be saved: something changed and nothing else runs.
    /// </summary>
    /// <returns><see langword="true"/> when they can.</returns>
    private bool CanSaveSettings() => !IsBusy && Settings.IsChanged;

    /// <summary>
    /// Puts the settings of the file back into the form.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRevertSettings))]
    private void RevertSettings() => LoadSettings();

    /// <summary>
    /// Tells whether the form differs from the file.
    /// </summary>
    /// <returns><see langword="true"/> when it does.</returns>
    private bool CanRevertSettings() => Settings.IsChanged;

    /// <summary>
    /// Reads the settings file into the form.
    /// </summary>
    private void LoadSettings()
    {
        try
        {
            Settings.Show(_settings.Load());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Settings.Problems = Texts.Format("Monitor.Settings.Unreadable", _settings.Path, ex.Message);
        }
    }

    /// <summary>
    /// Runs an action on the service and tells how it went.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <param name="doneKey">The text when it succeeded.</param>
    /// <returns>A task that completes when the action is over.</returns>
    private async Task RunAsync(Func<CancellationToken, Task> action, string doneKey)
    {
        IsBusy = true;
        Message = Texts.Get("Monitor.Working");
        try
        {
            await action(CancellationToken.None);
            Message = Texts.Get(doneKey);
        }
        catch (ElevationCancelledException)
        {
            Message = Texts.Get("Monitor.Error.Declined");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Message = Texts.Format("Monitor.Error.Failed", ex.Message);
        }
        finally
        {
            IsBusy = false;
            _refreshes = 0;
        }
    }

    /// <summary>
    /// Puts the state of the service and the answers of the server together.
    /// </summary>
    /// <returns>The key of the words and the condition.</returns>
    private (string Key, ServerCondition Condition) Describe()
    {
        var answers = Health is not null;
        return ServiceState switch
        {
            ServiceState.Running when answers => ("Running", ServerCondition.Good),
            ServiceState.Running => ("NotAnswering", ServerCondition.Bad),
            ServiceState.Starting => ("Starting", ServerCondition.Warning),
            ServiceState.Stopping => ("Stopping", ServerCondition.Warning),
            ServiceState.Unknown when answers => ("RunningServiceUnknown", ServerCondition.Warning),
            _ when answers => ("RunningManually", ServerCondition.Warning),
            ServiceState.Stopped => ("Stopped", ServerCondition.Bad),
            ServiceState.NotInstalled => ("NotInstalled", ServerCondition.Bad),
            _ => ("Unknown", ServerCondition.Unknown),
        };
    }

    /// <summary>
    /// Formats how long the server has been running, for example <c>2 d 03:14:05</c>.
    /// </summary>
    /// <param name="uptime">The time.</param>
    /// <returns>The text.</returns>
    private static string FormatUptime(TimeSpan uptime)
        => uptime.Days > 0
            ? Texts.Format("Monitor.Uptime.Days", uptime.Days, uptime.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture))
            : uptime.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
}
