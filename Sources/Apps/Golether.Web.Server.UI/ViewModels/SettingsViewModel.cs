using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Golether.Localization;
using Golether.Web.Server.Options;
using Golether.Web.Server.UI.Services;

namespace Golether.Web.Server.UI.ViewModels;

/// <summary>
/// The settings of the server as fields of a form. The fields hold text, so a half-typed number is not lost; the
/// values are checked by the same rules the server applies when it starts.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>
    /// The settings as they were loaded, to tell whether anything changed.
    /// </summary>
    private ServerSettings? _loaded;

    /// <summary>
    /// Gets or sets the address the server listens on.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    public partial string ListenAddress { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the port of the meetings (TCP) and of STUN (UDP).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    public partial string Port { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the server answers STUN.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    public partial bool EnableStun { get; set; }

    /// <summary>
    /// Gets or sets the largest number of connections at once.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    public partial string MaxConnections { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the largest number of rooms.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    public partial string MaxRooms { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the largest number of meetings waiting for one host.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    public partial string MaxPendingPerRoom { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the connections one address may open per minute.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    public partial string MaxConnectionsPerMinute { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the STUN requests one address may send per minute.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    public partial string MaxStunPerMinute { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the seconds a device has for its first message.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    public partial string FirstMessageTimeout { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the seconds a host has to answer a meeting.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    public partial string AnswerTimeout { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the seconds a silent host keeps its room.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    public partial string IdleTimeout { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the port of the local status address.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    public partial string StatusUrls { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how detailed the log of the service is.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    public partial string LogLevel { get; set; } = ServerSettings.DEFAULT_LOG_LEVEL;

    /// <summary>
    /// Gets the levels of the log to choose from.
    /// </summary>
    public IReadOnlyList<string> LogLevels => ServerSettings.LogLevels;

    /// <summary>
    /// Gets or sets what is wrong with the fields, one problem per line, or an empty text.
    /// </summary>
    [ObservableProperty]
    public partial string Problems { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the fields differ from the file.
    /// </summary>
    public bool IsChanged => _loaded is { } loaded && (!TryRead(out var current, out _) || current != loaded);

    /// <summary>
    /// Fills the fields from settings.
    /// </summary>
    /// <param name="settings">The settings.</param>
    public void Show(ServerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _loaded = settings;
        var r = settings.Rendezvous;
        ListenAddress = r.ListenAddress;
        Port = Number(r.Port);
        EnableStun = r.EnableStun;
        MaxConnections = Number(r.MaxConnections);
        MaxRooms = Number(r.MaxRooms);
        MaxPendingPerRoom = Number(r.MaxPendingPerRoom);
        MaxConnectionsPerMinute = Number(r.MaxConnectionsPerMinute);
        MaxStunPerMinute = Number(r.MaxStunPerMinute);
        FirstMessageTimeout = ServerSettingsStore.Seconds(r.FirstMessageTimeout);
        AnswerTimeout = ServerSettingsStore.Seconds(r.AnswerTimeout);
        IdleTimeout = ServerSettingsStore.Seconds(r.IdleTimeout);
        StatusUrls = settings.StatusUrls;
        LogLevel = ServerSettings.LogLevels.Contains(settings.LogLevel) ? settings.LogLevel : ServerSettings.DEFAULT_LOG_LEVEL;
        Problems = string.Empty;
        OnPropertyChanged(nameof(IsChanged));
    }

    /// <summary>
    /// Reads the fields and checks them by the rules of the server.
    /// </summary>
    /// <param name="settings">The settings, when they are valid.</param>
    /// <returns><see langword="true"/> when they are; otherwise <see cref="Problems"/> says what is wrong.</returns>
    public bool TryValidate(out ServerSettings settings)
    {
        if (!TryRead(out settings, out var problems))
        {
            Problems = string.Join(Environment.NewLine, problems);
            return false;
        }

        problems = [.. settings.Rendezvous.Validate()];
        if (!ServerSettings.IsValidStatusUrls(settings.StatusUrls))
        {
            problems.Add(Texts.Format("Monitor.Settings.BadStatusUrls", settings.StatusUrls));
        }

        Problems = string.Join(Environment.NewLine, problems);
        return problems.Count == 0;
    }

    /// <summary>
    /// Reads the fields into settings without checking the ranges.
    /// </summary>
    /// <param name="settings">The settings, when every number is a number.</param>
    /// <param name="problems">The fields that are not numbers.</param>
    /// <returns><see langword="true"/> when every number is a number.</returns>
    private bool TryRead(out ServerSettings settings, out List<string> problems)
    {
        var errors = new List<string>();
        var rendezvous = (_loaded?.Rendezvous ?? new RendezvousServerOptions()) with
        {
            ListenAddress = ListenAddress.Trim(),
            Port = Whole(Port, "Monitor.Settings.Port"),
            EnableStun = EnableStun,
            MaxConnections = Whole(MaxConnections, "Monitor.Settings.MaxConnections"),
            MaxRooms = Whole(MaxRooms, "Monitor.Settings.MaxRooms"),
            MaxPendingPerRoom = Whole(MaxPendingPerRoom, "Monitor.Settings.MaxPendingPerRoom"),
            MaxConnectionsPerMinute = Whole(MaxConnectionsPerMinute, "Monitor.Settings.MaxConnectionsPerMinute"),
            MaxStunPerMinute = Whole(MaxStunPerMinute, "Monitor.Settings.MaxStunPerMinute"),
            FirstMessageTimeout = TimeSpan.FromSeconds(Whole(FirstMessageTimeout, "Monitor.Settings.FirstMessageTimeout")),
            AnswerTimeout = TimeSpan.FromSeconds(Whole(AnswerTimeout, "Monitor.Settings.AnswerTimeout")),
            IdleTimeout = TimeSpan.FromSeconds(Whole(IdleTimeout, "Monitor.Settings.IdleTimeout")),
        };
        settings = new ServerSettings(rendezvous, StatusUrls.Trim(), LogLevel);
        problems = errors;
        return errors.Count == 0;

        int Whole(string text, string label)
        {
            if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            errors.Add(Texts.Format("Monitor.Settings.NotANumber", Texts.Get(label)));
            return 0;
        }
    }

    /// <summary>
    /// Formats a whole number for a field.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The text.</returns>
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
