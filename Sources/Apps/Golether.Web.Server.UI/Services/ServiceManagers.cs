using System.ComponentModel;
using Golether.Web.Server.Contracts;

namespace Golether.Web.Server.UI.Services;

/// <summary>
/// The state of the service of the server as the system reports it.
/// </summary>
public enum ServiceState
{
    /// <summary>
    /// The system could not be asked.
    /// </summary>
    Unknown,

    /// <summary>
    /// There is no such service on this computer.
    /// </summary>
    NotInstalled,

    /// <summary>
    /// The service is stopped.
    /// </summary>
    Stopped,

    /// <summary>
    /// The service is starting.
    /// </summary>
    Starting,

    /// <summary>
    /// The service is running.
    /// </summary>
    Running,

    /// <summary>
    /// The service is stopping.
    /// </summary>
    Stopping,
}

/// <summary>
/// Controls the service of the server through the tools of the system. Every action that changes something asks the
/// user for the rights of an administrator the way the system does it.
/// </summary>
public interface IServiceManager
{
    /// <summary>
    /// Asks the system for the state of the service; needs no special rights.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The state.</returns>
    Task<ServiceState> QueryAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Starts the service.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the system answered.</returns>
    /// <exception cref="ElevationCancelledException">The user declined.</exception>
    /// <exception cref="InvalidOperationException">The system refused; the message says why.</exception>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops the service.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the system answered.</returns>
    /// <exception cref="ElevationCancelledException">The user declined.</exception>
    /// <exception cref="InvalidOperationException">The system refused; the message says why.</exception>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Puts a new settings file in place and restarts the service if it runs, as one action: the user is asked once.
    /// </summary>
    /// <param name="source">The new settings, in a file the user can read.</param>
    /// <param name="target">The settings file of the server.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the system answered.</returns>
    /// <exception cref="ElevationCancelledException">The user declined.</exception>
    /// <exception cref="InvalidOperationException">The system refused; the message says why.</exception>
    Task ReplaceSettingsAndRestartAsync(string source, string target, CancellationToken cancellationToken);
}

/// <summary>
/// Picks the service manager of the system the monitor runs on.
/// </summary>
public static class ServiceManagers
{
    /// <summary>
    /// Returns the manager for this system.
    /// </summary>
    /// <param name="runner">Runs the tools of the system.</param>
    /// <returns>The manager.</returns>
    public static IServiceManager ForThisSystem(IProcessRunner runner)
        => OperatingSystem.IsWindows() ? new WindowsServiceManager(runner)
            : OperatingSystem.IsMacOS() ? new LaunchdServiceManager(runner, File.Exists)
            : new SystemdServiceManager(runner);

    /// <summary>
    /// Turns a failed tool into an exception with what the tool said, or its exit code when it said nothing. The
    /// monitor words the failure itself; the message is the evidence under it.
    /// </summary>
    /// <param name="action">What was being done; not shown, it makes the call sites read well.</param>
    /// <param name="result">The result of the tool.</param>
    /// <exception cref="InvalidOperationException">Always.</exception>
    internal static void Fail(string action, ProcessResult result)
        => throw new InvalidOperationException(result.Output.Trim() is { Length: > 0 } output ? output : "#" + result.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture), new InvalidOperationException(action));
}

/// <summary>
/// The Windows service <see cref="ServerContract.WindowsServiceName"/>: the service control manager of Windows
/// reports the state, PowerShell run as administrator changes it.
/// </summary>
/// <param name="runner">Runs the tools of the system.</param>
/// <param name="query">Asks the system for the state; replaced in tests.</param>
public sealed class WindowsServiceManager(IProcessRunner runner, Func<ServiceState>? query = null) : IServiceManager
{
    /// <inheritdoc />
    /// <remarks>
    /// The state is asked of the service control manager directly, not read from the text of <c>sc query</c>: that
    /// text is translated on Windows in other languages, so a parser of it does not see the state there.
    /// </remarks>
    public Task<ServiceState> QueryAsync(CancellationToken cancellationToken)
        => Task.FromResult((query ?? (() => WindowsServiceControl.Query(ServerContract.WindowsServiceName)))());

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
        => RunAsync("Start-Service -Name " + ServerContract.WindowsServiceName, "Start", cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
        => RunAsync("Stop-Service -Name " + ServerContract.WindowsServiceName + " -Force", "Stop", cancellationToken);

    /// <inheritdoc />
    public Task ReplaceSettingsAndRestartAsync(string source, string target, CancellationToken cancellationToken)
        => RunAsync(
            $"Copy-Item -LiteralPath {Literal(source)} -Destination {Literal(target)} -Force; "
            + $"$service = Get-Service -Name {ServerContract.WindowsServiceName} -ErrorAction SilentlyContinue; "
            + "if ($service -and $service.Status -eq 'Running') { Restart-Service -InputObject $service -Force }",
            "Save",
            cancellationToken);

    /// <summary>
    /// Writes a text as a PowerShell literal in single quotes.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The literal.</returns>
    public static string Literal(string text) => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>
    /// Runs a PowerShell script as administrator.
    /// </summary>
    /// <param name="script">The script.</param>
    /// <param name="action">What is being done, for the message of a failure.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the script finished.</returns>
    private async Task RunAsync(string script, string action, CancellationToken cancellationToken)
    {
        var exit = await runner.RunAsAdministratorAsync(
            "powershell.exe",
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", "$ErrorActionPreference = 'Stop'; " + script],
            cancellationToken).ConfigureAwait(false);
        if (exit != 0)
        {
            ServiceManagers.Fail(action, new ProcessResult(exit, string.Empty));
        }
    }
}

/// <summary>
/// The systemd unit <see cref="ServerContract.SystemdUnit"/> on Linux: <c>systemctl</c> reports the state,
/// <c>pkexec</c> asks for the rights to change it.
/// </summary>
/// <param name="runner">Runs the tools of the system.</param>
public sealed class SystemdServiceManager(IProcessRunner runner) : IServiceManager
{
    /// <summary>
    /// The exit code of <c>pkexec</c> when the user dismissed the question.
    /// </summary>
    private const int Dismissed = 126;

    /// <inheritdoc />
    public async Task<ServiceState> QueryAsync(CancellationToken cancellationToken)
    {
        ProcessResult result;
        try
        {
            result = await runner.RunAsync("systemctl", ["show", ServerContract.SystemdUnit, "--property=LoadState,ActiveState"], cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception)
        {
            return ServiceState.Unknown;
        }

        return Parse(result);
    }

    /// <summary>
    /// Reads the answer of <c>systemctl show</c>.
    /// </summary>
    /// <param name="result">The answer.</param>
    /// <returns>The state.</returns>
    public static ServiceState Parse(ProcessResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var values = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
        if (result.ExitCode != 0 || !values.TryGetValue("LoadState", out var load))
        {
            return ServiceState.Unknown;
        }

        if (load == "not-found")
        {
            return ServiceState.NotInstalled;
        }

        return values.GetValueOrDefault("ActiveState") switch
        {
            "active" or "reloading" => ServiceState.Running,
            "activating" => ServiceState.Starting,
            "deactivating" => ServiceState.Stopping,
            "inactive" or "failed" => ServiceState.Stopped,
            _ => ServiceState.Unknown,
        };
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
        => RunAsync(["systemctl", "start", ServerContract.SystemdUnit], "Start", cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
        => RunAsync(["systemctl", "stop", ServerContract.SystemdUnit], "Stop", cancellationToken);

    /// <inheritdoc />
    public Task ReplaceSettingsAndRestartAsync(string source, string target, CancellationToken cancellationToken)
        => RunAsync(
            ["sh", "-c", $"cp -- \"$1\" \"$2\" && if systemctl is-active --quiet {ServerContract.SystemdUnit}; then systemctl restart {ServerContract.SystemdUnit}; fi", "sh", source, target],
            "Save",
            cancellationToken);

    /// <summary>
    /// Runs a command as root through <c>pkexec</c>.
    /// </summary>
    /// <param name="command">The command and its arguments.</param>
    /// <param name="action">What is being done, for the message of a failure.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the command finished.</returns>
    private async Task RunAsync(IReadOnlyList<string> command, string action, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync("pkexec", command, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == Dismissed)
        {
            throw new ElevationCancelledException();
        }

        if (result.ExitCode != 0)
        {
            ServiceManagers.Fail(action, result);
        }
    }
}

/// <summary>
/// The launchd job <see cref="ServerContract.LaunchdLabel"/> on macOS: <c>launchctl</c> reports the state,
/// <c>osascript</c> asks for the password of an administrator to change it.
/// </summary>
/// <param name="runner">Runs the tools of the system.</param>
/// <param name="exists">Tells whether a file exists; replaced in tests.</param>
public sealed class LaunchdServiceManager(IProcessRunner runner, Func<string, bool> exists) : IServiceManager
{
    /// <summary>
    /// The target of the job in the system domain.
    /// </summary>
    private const string Target = "system/" + ServerContract.LaunchdLabel;

    /// <inheritdoc />
    public async Task<ServiceState> QueryAsync(CancellationToken cancellationToken)
    {
        if (!exists(ServerContract.LaunchdPlist))
        {
            return ServiceState.NotInstalled;
        }

        ProcessResult result;
        try
        {
            result = await runner.RunAsync("launchctl", ["print", Target], cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception)
        {
            return ServiceState.Unknown;
        }

        return Parse(result);
    }

    /// <summary>
    /// Reads the answer of <c>launchctl print</c> for an installed job: not loaded means stopped.
    /// </summary>
    /// <param name="result">The answer.</param>
    /// <returns>The state.</returns>
    public static ServiceState Parse(ProcessResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.ExitCode != 0)
        {
            return ServiceState.Stopped;
        }

        return result.Output.Contains("state = running", StringComparison.Ordinal) ? ServiceState.Running : ServiceState.Stopped;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
        => RunAsync($"launchctl bootstrap system {ServerContract.LaunchdPlist} 2>/dev/null; launchctl kickstart {Target}", "Start", cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
        => RunAsync($"launchctl bootout {Target}", "Stop", cancellationToken);

    /// <inheritdoc />
    public Task ReplaceSettingsAndRestartAsync(string source, string target, CancellationToken cancellationToken)
        => RunAsync(
            $"cp -- {Quote(source)} {Quote(target)} && if launchctl print {Target} >/dev/null 2>&1; then launchctl kickstart -k {Target}; fi",
            "Save",
            cancellationToken);

    /// <summary>
    /// Quotes a text for the shell in single quotes.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The quoted text.</returns>
    public static string Quote(string text) => "'" + text.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    /// <summary>
    /// Writes a shell command as the AppleScript that runs it with the rights of an administrator.
    /// </summary>
    /// <param name="command">The shell command.</param>
    /// <returns>The script for <c>osascript -e</c>.</returns>
    public static string AppleScript(string command)
        => "do shell script \"" + command.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\" with administrator privileges";

    /// <summary>
    /// Runs a shell command as root, after the system asked for the password.
    /// </summary>
    /// <param name="command">The shell command.</param>
    /// <param name="action">What is being done, for the message of a failure.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the command finished.</returns>
    private async Task RunAsync(string command, string action, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync("osascript", ["-e", AppleScript(command)], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 && result.Output.Contains("-128", StringComparison.Ordinal))
        {
            // "User canceled" in every language of macOS.
            throw new ElevationCancelledException();
        }

        if (result.ExitCode != 0)
        {
            ServiceManagers.Fail(action, result);
        }
    }
}
