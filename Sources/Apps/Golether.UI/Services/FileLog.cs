using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Golether.UI.Services;

/// <summary>
/// Writes the application log into <c>data/logs</c>, one file per day, so a problem can be looked at later and put
/// into a diagnostic report. Keys and tokens never reach the log: see <see cref="DiagnosticReport.Redact"/>.
/// </summary>
public sealed class FileLogProvider : ILoggerProvider
{
    /// <summary>
    /// How many daily files are kept.
    /// </summary>
    public const int KeepDays = 7;

    /// <summary>
    /// The largest size of a daily file; after it the log stops growing.
    /// </summary>
    public const long MaxFileBytes = 8 * 1024 * 1024;

    /// <summary>
    /// The log directory.
    /// </summary>
    private readonly string _directory;

    /// <summary>
    /// The time source.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Serializes writes.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// Whether writing failed and is not tried again.
    /// </summary>
    private bool _broken;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileLogProvider"/> class.
    /// </summary>
    /// <param name="directory">The log directory.</param>
    /// <param name="timeProvider">The time source.</param>
    public FileLogProvider(string directory, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        _timeProvider = timeProvider ?? TimeProvider.System;
        try
        {
            Directory.CreateDirectory(directory);
            foreach (var file in Directory.GetFiles(directory, "golether-*.log"))
            {
                if (_timeProvider.GetUtcNow() - File.GetLastWriteTimeUtc(file) > TimeSpan.FromDays(KeepDays))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _broken = true;
        }
    }

    /// <summary>
    /// Gets the file of the current day.
    /// </summary>
    public string CurrentFile => Path.Combine(_directory, $"golether-{_timeProvider.GetLocalNow():yyyy-MM-dd}.log");

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <summary>
    /// Appends a line, ignoring failures: a log must never stop the application.
    /// </summary>
    /// <param name="line">The line.</param>
    internal void Write(string line)
    {
        lock (_gate)
        {
            if (_broken)
            {
                return;
            }

            try
            {
                var file = CurrentFile;
                if (File.Exists(file) && new FileInfo(file).Length > MaxFileBytes)
                {
                    return;
                }

                File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _broken = true;
            }
        }
    }

    /// <summary>
    /// A logger of one category.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <param name="category">The category.</param>
    private sealed class FileLogger(FileLogProvider provider, string category) : ILogger
    {
        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || formatter is null)
            {
                return;
            }

            var text = formatter(state, exception);
            if (exception is not null)
            {
                text += " | " + exception.GetType().Name + ": " + exception.Message;
            }

            var time = provider._timeProvider.GetLocalNow().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var name = category.Split('.')[^1];
            provider.Write(DiagnosticReport.Redact($"{time} {Short(logLevel)} {name}: {text}"));
        }

        /// <summary>
        /// Returns the short name of a level.
        /// </summary>
        /// <param name="level">The level.</param>
        /// <returns>The name.</returns>
        private static string Short(LogLevel level) => level switch
        {
            LogLevel.Critical => "крит",
            LogLevel.Error => "ошиб",
            LogLevel.Warning => "пред",
            LogLevel.Information => "инфо",
            _ => "отлд",
        };
    }
}
