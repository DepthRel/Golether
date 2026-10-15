using System.ComponentModel;
using Golether.Web.Server.Contracts;
using Golether.Web.Server.UI.Enums;

namespace Golether.Web.Server.UI.Services;

/// <summary>
/// The systemd unit <see cref="ServerContract.SYSTEMD_UNIT"/> on Linux: <c>systemctl</c> reports the state,
/// <c>pkexec</c> asks for the rights to change it.
/// </summary>
/// <param name="runner">Runs the tools of the system.</param>
public sealed class SystemdServiceManager(IProcessRunner runner) : IServiceManager
{
    /// <summary>
    /// The exit code of <c>pkexec</c> when the user dismissed the question.
    /// </summary>
    private const int DISMISSED = 126;

    /// <inheritdoc />
    public async Task<ServiceState> QueryAsync(CancellationToken cancellationToken)
    {
        ProcessResult result;
        try
        {
            result = await runner.RunAsync("systemctl", ["show", ServerContract.SYSTEMD_UNIT, "--property=LoadState,ActiveState"], cancellationToken).ConfigureAwait(false);
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
        => RunAsync(["systemctl", "start", ServerContract.SYSTEMD_UNIT], "Start", cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
        => RunAsync(["systemctl", "stop", ServerContract.SYSTEMD_UNIT], "Stop", cancellationToken);

    /// <inheritdoc />
    public Task ReplaceSettingsAndRestartAsync(string source, string target, CancellationToken cancellationToken)
        => RunAsync(
            ["sh", "-c", $"cp -- \"$1\" \"$2\" && if systemctl is-active --quiet {ServerContract.SYSTEMD_UNIT}; then systemctl restart {ServerContract.SYSTEMD_UNIT}; fi", "sh", source, target],
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
        if (result.ExitCode == DISMISSED)
        {
            throw new ElevationCancelledException();
        }

        if (result.ExitCode != 0)
        {
            ServiceManagers.Fail(action, result);
        }
    }
}
