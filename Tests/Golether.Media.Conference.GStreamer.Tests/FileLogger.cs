using Microsoft.Extensions.Logging;

namespace Golether.Media.Conference.GStreamer.Tests;

/// <summary>
/// A logger that appends to a file (diagnostics of the native tests); does nothing without a file.
/// </summary>
/// <typeparam name="T">The category.</typeparam>
/// <param name="path">The log file or <see langword="null"/>.</param>
internal sealed class FileLogger<T>(string? path) : ILogger<T>
{
    /// <summary>
    /// Serializes writes.
    /// </summary>
    private static readonly Lock Gate = new();

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => path is not null;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (path is null)
        {
            return;
        }

        lock (Gate)
        {
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {logLevel} {formatter(state, exception)} {exception}{Environment.NewLine}");
        }
    }
}
