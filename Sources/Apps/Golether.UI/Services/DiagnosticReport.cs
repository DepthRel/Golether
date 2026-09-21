using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Golether.Components.Installation;
using Golether.Localization;
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
        report.AppendLine(Texts.Format("Report.Title", now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)));
        report.AppendLine(Texts.Format("Report.Version", version));
        report.AppendLine(Texts.Format("Report.System", Environment.OSVersion.VersionString, Environment.ProcessorCount, Environment.Version));
        report.AppendLine(Texts.Format("Report.Device", deviceFingerprint));
        report.AppendLine();

        report.AppendLine(Texts.Get("Report.Components"));
        foreach (var component in components ?? [])
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"  {component.Id}: {component.Source}{(component.Path is { Length: > 0 } path ? " — " + Redact(path) : string.Empty)}");
        }

        if (notes is { Count: > 0 })
        {
            report.AppendLine();
            report.AppendLine(Texts.Get("Report.State"));
            foreach (var note in notes)
            {
                report.AppendLine("  " + Redact(note));
            }
        }

        report.AppendLine();
        if (snapshot is null)
        {
            report.AppendLine(Texts.Get("Report.NoSession"));
        }
        else
        {
            report.AppendLine(Texts.Format(
                "Report.Session",
                Texts.Get(snapshot.IsHost ? "Report.Role.Host" : "Report.Role.Participant"),
                snapshot.State,
                snapshot.Participants.Count));
            var file = snapshot.Media is { } media
                ? Texts.Format("Report.File.Details", Path.GetExtension(media.FileName), media.Length.ToString(CultureInfo.InvariantCulture))
                    + (snapshot.UsesLocalCopy ? Texts.Get("Report.File.LocalCopy") : string.Empty)
                : Texts.Get("Report.File.None");
            report.AppendLine(Texts.Format("Report.File", file));
            var playing = snapshot.Playback is { } playback
                ? Texts.Format("Report.Playback.Details", playback.State, playback.Position, playback.Cause, playback.Version.ToString(CultureInfo.InvariantCulture))
                : Texts.Get("Report.Playback.None");
            report.AppendLine(Texts.Format("Report.Playback", playing));
            report.AppendLine(Texts.Format(
                "Report.Clock",
                Whole(snapshot.RoundTrip?.TotalMilliseconds ?? 0),
                Whole(snapshot.ClockUncertainty?.TotalMilliseconds ?? 0)));
            foreach (var participant in snapshot.Participants)
            {
                var status = participant.Status;
                report.AppendLine(Texts.Format(
                    "Report.Participant",
                    participant.Info.PeerId.ToShortString(),
                    participant.IsLocal ? Texts.Get("Report.You") : string.Empty,
                    status?.Position,
                    Whole(status?.Drift.TotalMilliseconds ?? 0),
                    Whole(status?.CacheAhead.TotalSeconds ?? 0),
                    (status?.RoundTripMilliseconds ?? 0).ToString(CultureInfo.InvariantCulture),
                    Texts.Get(status?.MicrophoneOff == true ? "Report.Off" : "Report.On"),
                    Texts.Get(status?.CameraOff == true ? "Report.Off" : "Report.On")));
            }
        }

        report.AppendLine();
        report.AppendLine(Texts.Format("Report.Log", (log?.Count ?? 0).ToString(CultureInfo.InvariantCulture)));
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
            return [Texts.Format("Report.LogNotRead", ex.Message)];
        }
    }

    /// <summary>
    /// Rounds a number of milliseconds or seconds to a whole number, written the same way in every language.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The text.</returns>
    private static string Whole(double value) => value.ToString("0", CultureInfo.InvariantCulture);

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
