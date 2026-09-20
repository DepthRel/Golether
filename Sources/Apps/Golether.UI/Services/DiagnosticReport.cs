using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Golether.Components.Installation;
using Golether.Session;

namespace Golether.UI.Services;

/// <summary>
/// Builds a report that can be sent to somebody who helps with a problem: what is installed, how the session goes
/// and the tail of the application log. Keys, tokens and passwords are cut out.
/// </summary>
public static partial class DiagnosticReport
{
    /// <summary>
    /// How many log lines are included.
    /// </summary>
    public const int LogLines = 400;

    /// <summary>
    /// Removes secrets from a line: long hexadecimal strings (device identifiers, tokens, tickets) keep only their
    /// first characters, and credentials in relay addresses disappear.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The text without secrets.</returns>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var clean = RelayCredentials().Replace(text, "turn://…@");
        clean = LongHex().Replace(clean, m => m.Value[..8] + "…");
        return Base64Secret().Replace(clean, m => m.Value[..6] + "…");
    }

    /// <summary>
    /// Builds the report.
    /// </summary>
    /// <param name="version">The application version.</param>
    /// <param name="deviceFingerprint">The short fingerprint of this device.</param>
    /// <param name="components">The state of the native components.</param>
    /// <param name="snapshot">The session, or <see langword="null"/> when none runs.</param>
    /// <param name="notes">Extra lines about the player and the conference.</param>
    /// <param name="log">The tail of the application log.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The report text.</returns>
    public static string Build(
        string version,
        string deviceFingerprint,
        IReadOnlyList<ComponentStatus> components,
        SessionSnapshot? snapshot,
        IReadOnlyList<string> notes,
        IReadOnlyList<string> log,
        DateTimeOffset now)
    {
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"Отчёт Golether от {now:yyyy-MM-dd HH:mm:ss zzz}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Версия: {version}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Система: {Environment.OSVersion.VersionString}, {Environment.ProcessorCount} ядер, .NET {Environment.Version}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Устройство: {deviceFingerprint}");
        report.AppendLine();

        report.AppendLine("Компоненты:");
        foreach (var component in components ?? [])
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"  {component.Id}: {component.Source}{(component.Path is { Length: > 0 } path ? " — " + Redact(path) : string.Empty)}");
        }

        if (notes is { Count: > 0 })
        {
            report.AppendLine();
            report.AppendLine("Состояние:");
            foreach (var note in notes)
            {
                report.AppendLine("  " + Redact(note));
            }
        }

        report.AppendLine();
        if (snapshot is null)
        {
            report.AppendLine("Сеанс: не запущен.");
        }
        else
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"Сеанс: {(snapshot.IsHost ? "ведущий" : "участник")}, состояние {snapshot.State}, участников {snapshot.Participants.Count}");
            report.AppendLine(CultureInfo.InvariantCulture, $"  Файл: {(snapshot.Media is { } media ? $"{Path.GetExtension(media.FileName)}, {media.Length} байт{(snapshot.UsesLocalCopy ? ", локальная копия" : string.Empty)}" : "не выбран")}");
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  Воспроизведение: {(snapshot.Playback is { } playback ? $"{playback.State}, позиция {playback.Position}, причина {playback.Cause}, версия {playback.Version}" : "нет")}");
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  Часы: пинг {snapshot.RoundTrip?.TotalMilliseconds ?? 0:0} мс, неопределённость {snapshot.ClockUncertainty?.TotalMilliseconds ?? 0:0} мс");
            foreach (var participant in snapshot.Participants)
            {
                var status = participant.Status;
                report.AppendLine(CultureInfo.InvariantCulture,
                    $"  · {participant.Info.PeerId.ToShortString()}{(participant.IsLocal ? " (вы)" : string.Empty)}: " +
                    $"позиция {status?.Position}, расхождение {status?.Drift.TotalMilliseconds ?? 0:0} мс, буфер {status?.CacheAhead.TotalSeconds ?? 0:0} с, " +
                    $"пинг {status?.RoundTripMilliseconds ?? 0} мс, микрофон {(status?.MicrophoneOff == true ? "выкл" : "вкл")}, камера {(status?.CameraOff == true ? "выкл" : "вкл")}");
            }
        }

        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"Журнал (последние {log?.Count ?? 0} строк):");
        foreach (var line in log ?? [])
        {
            report.AppendLine(Redact(line));
        }

        return report.ToString();
    }

    /// <summary>
    /// Reads the tail of a log file, ignoring a missing or busy file.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="lines">How many lines to keep.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> ReadLogTail(string file, int lines = LogLines)
    {
        try
        {
            if (!File.Exists(file))
            {
                return [];
            }

            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var tail = new Queue<string>(lines);
            while (reader.ReadLine() is { } line)
            {
                if (tail.Count == lines)
                {
                    tail.Dequeue();
                }

                tail.Enqueue(line);
            }

            return [.. tail];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [$"Журнал не прочитан: {ex.Message}"];
        }
    }

    /// <summary>
    /// Matches the credentials of a relay address.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"turns?://[^@\s/]+@", RegexOptions.IgnoreCase)]
    private static partial Regex RelayCredentials();

    /// <summary>
    /// Matches long hexadecimal strings: device identifiers, tokens and tickets.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex("[0-9a-fA-F]{32,}")]
    private static partial Regex LongHex();

    /// <summary>
    /// Matches long base64 strings, for example keys.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"\b[A-Za-z0-9+/]{40,}={0,2}\b")]
    private static partial Regex Base64Secret();
}
