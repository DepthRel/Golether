using Golether.Components.Catalog;
using Golether.Components.Installation;
using Golether.Core.Identity;
using Golether.Core.Media;
using Golether.Core.Playback;
using Golether.Core.Session;
using Golether.Session;
using Golether.UI.Services;
using Microsoft.Extensions.Logging;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of <see cref="DiagnosticReport"/>.
/// </summary>
public sealed class DiagnosticReportTests
{
    /// <summary>
    /// The host device.
    /// </summary>
    private static readonly PeerId Host = PeerId.Parse(new string('a', 64));

    /// <summary>
    /// Keys, tokens and relay passwords never reach the report.
    /// </summary>
    [Fact]
    public void Redact_RemovesSecrets()
    {
        Assert.Equal("Пир aaaaaaaa… подключён", DiagnosticReport.Redact($"Пир {new string('a', 64)} подключён"));
        Assert.Equal("relay turn://…@192.168.1.2:47801?transport=tcp", DiagnosticReport.Redact("relay turn://golether-x:secret_1@192.168.1.2:47801?transport=tcp"));
        Assert.Equal("ключ SGVsbG…=", DiagnosticReport.Redact("ключ SGVsbG8gd29ybGQgdGhpcyBpcyBhIHNlY3JldCBrZXkgdmFsdWU="));
        Assert.Equal("порт 47800, позиция 00:12:35", DiagnosticReport.Redact("порт 47800, позиция 00:12:35"));
        Assert.Equal(string.Empty, DiagnosticReport.Redact(null));
    }

    /// <summary>
    /// The report names the components, the session and the log, and keeps no secrets or file names.
    /// </summary>
    [Fact]
    public void Build_DescribesTheState()
    {
        var guest = PeerId.Parse(new string('b', 64));
        var snapshot = new SessionSnapshot
        {
            IsHost = true,
            State = SessionState.Active,
            SessionName = "Вечер кино",
            HostPeerId = Host,
            Participants =
            [
                new ParticipantView(new ParticipantInfo(Host, "Вы", true), new ParticipantStatus { PeerId = Host, Position = TimeSpan.FromMinutes(5) }, true),
                new ParticipantView(new ParticipantInfo(guest, "Марина", false), new ParticipantStatus { PeerId = guest, Drift = TimeSpan.FromMilliseconds(-120), CameraOff = true }, false),
            ],
            Playback = PlaybackState.Initial(Host, 0) with { State = PlayState.Playing, Position = TimeSpan.FromMinutes(5) },
            Media = new MediaDescriptor { FileName = "Очень личное название.mkv", Length = 1234, QuickId = new string('c', 64) },
            RoundTrip = TimeSpan.FromMilliseconds(42),
        };

        var text = DiagnosticReport.Build(
            "0.1.0",
            "A249-B9CC",
            [new ComponentStatus(ComponentId.Video, ComponentSource.Bundled, "app/native/libmpv-2.dll", null, null)],
            snapshot,
            ["Плеер: работает"],
            [$"12:00:00 инфо HostSession: пир {new string('d', 64)} вошёл"],
            new DateTimeOffset(2026, 9, 18, 21, 5, 0, TimeSpan.FromHours(3)));

        Assert.Contains("Версия: 0.1.0", text, StringComparison.Ordinal);
        Assert.Contains("Устройство: A249-B9CC", text, StringComparison.Ordinal);
        Assert.Contains("Video: Bundled — app/native/libmpv-2.dll", text, StringComparison.Ordinal);
        Assert.Contains("Плеер: работает", text, StringComparison.Ordinal);
        Assert.Contains("Сеанс: ведущий, состояние Active, участников 2", text, StringComparison.Ordinal);
        Assert.Contains("Файл: .mkv, 1234 байт", text, StringComparison.Ordinal);
        Assert.Contains("расхождение -120 мс", text, StringComparison.Ordinal);
        Assert.Contains("камера выкл", text, StringComparison.Ordinal);
        Assert.Contains("dddddddd…", text, StringComparison.Ordinal);

        // No file name of the film, no full identifiers.
        Assert.DoesNotContain("Очень личное название", text, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('c', 64), text, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('d', 64), text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The log tail returns the last lines and survives a missing file.
    /// </summary>
    [Fact]
    public async Task ReadLogTail_TakesTheLastLines()
    {
        var folder = Directory.CreateTempSubdirectory("golether-log-");
        try
        {
            var file = Path.Combine(folder.FullName, "golether.log");
            await File.WriteAllLinesAsync(file, Enumerable.Range(1, 50).Select(i => $"строка {i}"), TestContext.Current.CancellationToken);

            var tail = DiagnosticReport.ReadLogTail(file, 5);

            Assert.Equal(["строка 46", "строка 47", "строка 48", "строка 49", "строка 50"], tail);
            Assert.Empty(DiagnosticReport.ReadLogTail(Path.Combine(folder.FullName, "missing.log")));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The log writer creates a daily file, writes lines without secrets and keeps working when the folder is gone.
    /// </summary>
    [Fact]
    public void FileLog_WritesWithoutSecrets()
    {
        var folder = Directory.CreateTempSubdirectory("golether-filelog-");
        try
        {
            using var provider = new FileLogProvider(folder.FullName);
            var logger = provider.CreateLogger("Golether.Session.HostSession");

            logger.LogInformation("Пир {Peer} вошёл", new string('e', 64));
            logger.LogDebug("не попадёт в файл");
            logger.LogWarning(new IOException("сеть недоступна"), "Разрыв");

            var lines = File.ReadAllLines(provider.CurrentFile);
            Assert.Equal(2, lines.Length);
            Assert.EndsWith("инфо HostSession: Пир eeeeeeee… вошёл", lines[0], StringComparison.Ordinal);
            Assert.Contains("пред HostSession: Разрыв | IOException: сеть недоступна", lines[1], StringComparison.Ordinal);
            Assert.DoesNotContain(new string('e', 64), string.Join('\n', lines), StringComparison.Ordinal);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
