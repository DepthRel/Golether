using Golether.Tunnels.AmneziaWG.Configuration;
using Golether.Tunnels.AmneziaWG.Control;

namespace Golether.Tunnels.AmneziaWG.Tests;

/// <summary>
/// Runs the real <see cref="QuickScript"/> in a POSIX shell with a stand-in for awg-quick that writes down how it was
/// called.
/// </summary>
public sealed class QuickScriptTests : IDisposable
{
    /// <summary>
    /// The folder of a test.
    /// </summary>
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("golether-quick-");

    /// <inheritdoc />
    public void Dispose() => _directory.Delete(recursive: true);

    /// <summary>
    /// A configuration the application wrote reaches awg-quick as a copy in the folder of root: "up" first takes a
    /// tunnel of the same name down, "down" removes the copy.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task OurConfiguration_RunsFromTheCopy()
    {
        var shell = FindShell();
        Assert.SkipWhen(shell is null, "There is no POSIX shell on this device.");
        var token = TestContext.Current.CancellationToken;
        var (quick, log) = CreateStandIn();
        var source = Path.Combine(_directory.FullName, "golether0.conf");
        var written = HostTunnelInterface.Create().BuildConfiguration([]).Render();
        await File.WriteAllTextAsync(source, written, token);
        var system = Path.Combine(_directory.FullName, "system");
        var copy = Path.Combine(system, "golether0.conf");

        var up = await RunAsync(shell!, QuickScript.BuildArguments("up", source, "golether0", quick, system), token);

        Assert.Equal(0, up.ExitCode);
        Assert.Equal(written, await File.ReadAllTextAsync(copy, token));
        var calls = await File.ReadAllLinesAsync(log, token);
        Assert.Equal(["down", "up"], calls.Select(line => line.Split(' ')[0]));
        Assert.All(calls, line => Assert.EndsWith("golether0.conf", line, StringComparison.Ordinal));
        Assert.DoesNotContain(calls, line => line.Contains(source, StringComparison.Ordinal));

        var down = await RunAsync(shell!, QuickScript.BuildArguments("down", source, "golether0", quick, system), token);

        Assert.Equal(0, down.ExitCode);
        Assert.False(File.Exists(copy));
    }

    /// <summary>
    /// A configuration with a line the application never writes (a command to run as root on the way up) is refused
    /// before awg-quick is started, and nothing of it stays in the folder of root.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ConfigurationWithACommand_IsRefused()
    {
        var shell = FindShell();
        Assert.SkipWhen(shell is null, "There is no POSIX shell on this device.");
        var token = TestContext.Current.CancellationToken;
        var (quick, log) = CreateStandIn();
        var source = Path.Combine(_directory.FullName, "golether0.conf");
        var written = HostTunnelInterface.Create().BuildConfiguration([]).Render();
        await File.WriteAllTextAsync(source, written.Replace("[Interface]\n", "[Interface]\n  postup = touch pwned\n", StringComparison.Ordinal), token);
        var system = Path.Combine(_directory.FullName, "system");

        var result = await RunAsync(shell!, QuickScript.BuildArguments("up", source, "golether0", quick, system), token);

        Assert.Equal(QuickScript.REJECTED, result.ExitCode);
        Assert.False(File.Exists(log));
        Assert.Empty(Directory.GetFiles(system));
    }

    /// <summary>
    /// Finds a POSIX shell: the one of the system, or the one of Git on Windows.
    /// </summary>
    /// <returns>The shell, or <see langword="null"/> when there is none.</returns>
    private static string? FindShell()
    {
        var shell = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "sh.exe")
            : "/bin/sh";
        return File.Exists(shell) ? shell : null;
    }

    /// <summary>
    /// Runs the script.
    /// </summary>
    /// <param name="shell">The shell.</param>
    /// <param name="arguments">The arguments of the script.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result.</returns>
    private static Task<ProcessResult> RunAsync(string shell, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        => new ProcessRunner().RunAsync(shell, arguments, TimeSpan.FromSeconds(30), cancellationToken);

    /// <summary>
    /// Creates the stand-in for awg-quick: it appends its verb and configuration to a log.
    /// </summary>
    /// <returns>The program and its log.</returns>
    private (string Program, string Log) CreateStandIn()
    {
        var program = Path.Combine(_directory.FullName, "awg-quick");
        var log = Path.Combine(_directory.FullName, "calls.log");
        File.WriteAllText(program, $"#!/bin/sh\necho \"$1 $2\" >> '{log.Replace('\\', '/')}'\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return (program, log);
    }
}
