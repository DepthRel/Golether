using Golether.Web.Server.Contracts;
using Golether.Web.Server.UI.Enums;

namespace Golether.Web.Server.UI.Services;

/// <summary>
/// The Windows service <see cref="ServerContract.WINDOWS_SERVICE_NAME"/>: the service control manager of Windows
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
        => Task.FromResult((query ?? (() => WindowsServiceControl.Query(ServerContract.WINDOWS_SERVICE_NAME)))());

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
        => RunAsync("Start-Service -Name " + ServerContract.WINDOWS_SERVICE_NAME, "Start", cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
        => RunAsync("Stop-Service -Name " + ServerContract.WINDOWS_SERVICE_NAME + " -Force", "Stop", cancellationToken);

    /// <inheritdoc />
    public Task ReplaceSettingsAndRestartAsync(string source, string target, CancellationToken cancellationToken)
        => RunAsync(
            $"Copy-Item -LiteralPath {Literal(source)} -Destination {Literal(target)} -Force; "
            + $"$service = Get-Service -Name {ServerContract.WINDOWS_SERVICE_NAME} -ErrorAction SilentlyContinue; "
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
