using NSubstitute;
using Golether.Web.Server.Contracts;
using Golether.Web.Server.UI.Enums;
using Golether.Web.Server.UI.Services;

namespace Golether.Web.Server.UI.Tests;

/// <summary>
/// Tests of the service managers: they read what the tools of the system answer and build the commands run with the
/// rights of an administrator. The tools themselves are replaced by a stub.
/// </summary>
public sealed class ServiceManagerTests
{
    /// <summary>
    /// The states of the service control manager map to the states of the monitor; a paused service counts as
    /// running, a pausing one as stopping.
    /// </summary>
    /// <param name="native">The number of Windows.</param>
    /// <param name="expected">The state.</param>
    [Theory]
    [InlineData(1u, ServiceState.Stopped)]
    [InlineData(2u, ServiceState.Starting)]
    [InlineData(3u, ServiceState.Stopping)]
    [InlineData(4u, ServiceState.Running)]
    [InlineData(5u, ServiceState.Starting)]
    [InlineData(6u, ServiceState.Stopping)]
    [InlineData(7u, ServiceState.Running)]
    [InlineData(0u, ServiceState.Unknown)]
    public void Windows_MapsTheStatesOfTheSystem(uint native, ServiceState expected)
        => Assert.Equal(expected, WindowsServiceControl.FromNative(native));

    /// <summary>
    /// On Windows the state is asked of the system itself, whatever language its tools speak: a service every
    /// Windows has runs, a made-up one is not installed. Elsewhere the answer is "unknown".
    /// </summary>
    [Fact]
    public void Windows_AsksTheSystemItself()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(ServiceState.Unknown, WindowsServiceControl.Query("EventLog"));
            return;
        }

        Assert.Equal(ServiceState.Running, WindowsServiceControl.Query("EventLog"));
        Assert.Equal(ServiceState.NotInstalled, WindowsServiceControl.Query("GoletherNoSuchService" + Guid.NewGuid().ToString("N")));
    }

    /// <summary>
    /// The manager reports what the system says and runs no tool for it.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Windows_QueriesWithoutATool()
    {
        var runner = Substitute.For<IProcessRunner>();

        Assert.Equal(ServiceState.Stopping, await new WindowsServiceManager(runner, () => ServiceState.Stopping).QueryAsync(TestContext.Current.CancellationToken));
        Assert.Empty(runner.ReceivedCalls());
    }

    /// <summary>
    /// The Windows actions run PowerShell as administrator; paths with spaces and quotes reach it unchanged.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Windows_RunsTheActionsAsAdministrator()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsAdministratorAsync(default!, default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(0);
        var manager = new WindowsServiceManager(runner);

        await manager.StartAsync(TestContext.Current.CancellationToken);
        await manager.ReplaceSettingsAndRestartAsync(@"C:\Temp\new settings.json", @"C:\Program Files\Golether Server\config\appsettings.json", TestContext.Current.CancellationToken);

        var calls = runner.ReceivedCalls().Select(call => (string)call.GetArguments()[0]! + " " + string.Join(" ", (IReadOnlyList<string>)call.GetArguments()[1]!)).ToArray();
        Assert.Equal(2, calls.Length);
        Assert.All(calls, call => Assert.StartsWith("powershell.exe -NoProfile -NonInteractive", call, StringComparison.Ordinal));
        Assert.Contains("Start-Service -Name " + ServerContract.WINDOWS_SERVICE_NAME, calls[0], StringComparison.Ordinal);
        Assert.Contains(@"Copy-Item -LiteralPath 'C:\Temp\new settings.json' -Destination 'C:\Program Files\Golether Server\config\appsettings.json'", calls[1], StringComparison.Ordinal);
        Assert.Contains("Restart-Service", calls[1], StringComparison.Ordinal);
        Assert.Equal("'it''s'", WindowsServiceManager.Literal("it's"));
    }

    /// <summary>
    /// A PowerShell that failed is reported as a failure, with its exit code as the evidence.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Windows_ReportsAFailedAction()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsAdministratorAsync(default!, default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(1);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new WindowsServiceManager(runner).StopAsync(TestContext.Current.CancellationToken));
        Assert.Equal("#1", error.Message);
    }

    /// <summary>
    /// systemd: the state comes from <c>systemctl show</c>; an unknown unit is a service that is not installed.
    /// </summary>
    [Fact]
    public void Systemd_ReadsTheState()
    {
        Assert.Equal(ServiceState.Running, SystemdServiceManager.Parse(new ProcessResult(0, "LoadState=loaded\nActiveState=active\n")));
        Assert.Equal(ServiceState.Stopped, SystemdServiceManager.Parse(new ProcessResult(0, "LoadState=loaded\nActiveState=failed\n")));
        Assert.Equal(ServiceState.Starting, SystemdServiceManager.Parse(new ProcessResult(0, "LoadState=loaded\nActiveState=activating\n")));
        Assert.Equal(ServiceState.NotInstalled, SystemdServiceManager.Parse(new ProcessResult(0, "LoadState=not-found\nActiveState=inactive\n")));
        Assert.Equal(ServiceState.Unknown, SystemdServiceManager.Parse(new ProcessResult(1, string.Empty)));
    }

    /// <summary>
    /// systemd: the actions run through <c>pkexec</c>; a dismissed question is a cancellation, not a failure, and the
    /// paths travel as arguments, never inside the script.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Systemd_AsksThroughPkexec()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync("pkexec", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(new ProcessResult(0, string.Empty), new ProcessResult(126, string.Empty));
        var manager = new SystemdServiceManager(runner);

        await manager.ReplaceSettingsAndRestartAsync("/tmp/a b.json", "/opt/golether-server/config/appsettings.json", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ElevationCancelledException>(() => manager.StopAsync(TestContext.Current.CancellationToken));

        var first = (IReadOnlyList<string>)runner.ReceivedCalls().First().GetArguments()[1]!;
        Assert.Equal("sh", first[0]);
        Assert.DoesNotContain("/tmp/a b.json", first[2], StringComparison.Ordinal);
        Assert.Equal(["/tmp/a b.json", "/opt/golether-server/config/appsettings.json"], first.Skip(4));
    }

    /// <summary>
    /// launchd: an installed job that is not loaded is stopped; the command is wrapped into AppleScript with its
    /// quotes escaped; "User canceled" (-128) is a cancellation.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Launchd_ReadsTheStateAndAsksForThePassword()
    {
        Assert.Equal(ServiceState.Running, LaunchdServiceManager.Parse(new ProcessResult(0, "system/org.golether.server = {\n\tstate = running\n")));
        Assert.Equal(ServiceState.Stopped, LaunchdServiceManager.Parse(new ProcessResult(113, "Could not find service")));
        Assert.Equal("do shell script \"cp -- 'a\\\"b' x\" with administrator privileges", LaunchdServiceManager.AppleScript("cp -- 'a\"b' x"));
        Assert.Equal("'it'\\''s'", LaunchdServiceManager.Quote("it's"));

        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync("osascript", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(new ProcessResult(1, "execution error: User canceled. (-128)"));
        var manager = new LaunchdServiceManager(runner, _ => true);
        await Assert.ThrowsAsync<ElevationCancelledException>(() => manager.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ServiceState.NotInstalled, await new LaunchdServiceManager(runner, _ => false).QueryAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The arguments of a Windows command line reach the program unchanged, spaces and quotes included.
    /// </summary>
    /// <param name="argument">The argument.</param>
    /// <param name="expected">How it is written.</param>
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("two words", "\"two words\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"C:\dir with space\", "\"C:\\dir with space\\\\\"")]
    [InlineData("", "\"\"")]
    public void Quote_KeepsTheArgumentWhole(string argument, string expected)
        => Assert.Equal(expected, ProcessRunner.Quote(argument));
}
