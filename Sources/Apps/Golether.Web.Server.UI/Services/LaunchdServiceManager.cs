using System.ComponentModel;
using Golether.Web.Server.Contracts;
using Golether.Web.Server.UI.Enums;

namespace Golether.Web.Server.UI.Services;

/// <summary>
/// The launchd job <see cref="ServerContract.LAUNCHD_LABEL"/> on macOS: <c>launchctl</c> reports the state,
/// <c>osascript</c> asks for the password of an administrator to change it.
/// </summary>
/// <param name="runner">Runs the tools of the system.</param>
/// <param name="exists">Tells whether a file exists; replaced in tests.</param>
public sealed class LaunchdServiceManager(IProcessRunner runner, Func<string, bool> exists) : IServiceManager
{
    /// <summary>
    /// The target of the job in the system domain.
    /// </summary>
    private const string TARGET = "system/" + ServerContract.LAUNCHD_LABEL;

    /// <inheritdoc />
    public async Task<ServiceState> QueryAsync(CancellationToken cancellationToken)
    {
        if (!exists(ServerContract.LAUNCHD_PLIST))
        {
            return ServiceState.NotInstalled;
        }

        ProcessResult result;
        try
        {
            result = await runner.RunAsync("launchctl", ["print", TARGET], cancellationToken).ConfigureAwait(false);
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
        => RunAsync($"launchctl bootstrap system {ServerContract.LAUNCHD_PLIST} 2>/dev/null; launchctl kickstart {TARGET}", "Start", cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
        => RunAsync($"launchctl bootout {TARGET}", "Stop", cancellationToken);

    /// <inheritdoc />
    public Task ReplaceSettingsAndRestartAsync(string source, string target, CancellationToken cancellationToken)
        => RunAsync(
            $"cp -- {Quote(source)} {Quote(target)} && if launchctl print {TARGET} >/dev/null 2>&1; then launchctl kickstart -k {TARGET}; fi",
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
