using System.Globalization;
using System.Reflection;
using Golether.Components.Catalog;
using Golether.Session;

namespace Golether.UI.Services;

/// <summary>
/// Writes the report into <c>data/logs</c>.
/// </summary>
public sealed class DiagnosticsWriter : IDiagnosticsWriter
{
    /// <summary>
    /// The log directory.
    /// </summary>
    private readonly string _logsDirectory;

    /// <summary>
    /// The components.
    /// </summary>
    private readonly IComponentService _components;

    /// <summary>
    /// The fingerprint of this device.
    /// </summary>
    private readonly string _fingerprint;

    /// <summary>
    /// The current log file.
    /// </summary>
    private readonly Func<string> _logFile;

    /// <summary>
    /// The time source.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticsWriter"/> class.
    /// </summary>
    /// <param name="logsDirectory">The log directory.</param>
    /// <param name="components">The components.</param>
    /// <param name="fingerprint">The fingerprint of this device.</param>
    /// <param name="logFile">Returns the current log file.</param>
    /// <param name="timeProvider">The time source.</param>
    public DiagnosticsWriter(
        string logsDirectory,
        IComponentService components,
        string fingerprint,
        Func<string> logFile,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logsDirectory);
        _logsDirectory = logsDirectory;
        _components = components ?? throw new ArgumentNullException(nameof(components));
        _fingerprint = fingerprint;
        _logFile = logFile ?? throw new ArgumentNullException(nameof(logFile));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<string> SaveAsync(SessionSnapshot? snapshot, IReadOnlyList<string> notes)
    {
        var now = _timeProvider.GetLocalNow();
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "неизвестна";
        var statuses = new[] { ComponentId.Video, ComponentId.Conference, ComponentId.Tunnel }.Select(_components.GetStatus).ToArray();
        var text = DiagnosticReport.Build(
            version,
            _fingerprint,
            statuses,
            snapshot,
            notes ?? [],
            DiagnosticReport.ReadLogTail(_logFile()),
            now);
        Directory.CreateDirectory(_logsDirectory);
        var path = Path.Combine(_logsDirectory, $"golether-отчёт-{now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture)}.txt");
        await File.WriteAllTextAsync(path, text, System.Text.Encoding.UTF8).ConfigureAwait(false);
        return path;
    }
}
