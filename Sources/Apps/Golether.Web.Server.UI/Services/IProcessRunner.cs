using System.ComponentModel;

namespace Golether.Web.Server.UI.Services;

/// <summary>
/// Runs the tools of the system that control the service (<c>sc</c>, <c>powershell</c>, <c>systemctl</c>,
/// <c>pkexec</c>, <c>launchctl</c>, <c>osascript</c>).
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs a program with the rights of the user and waits for it.
    /// </summary>
    /// <param name="fileName">The program.</param>
    /// <param name="arguments">The arguments, each passed as it is.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The exit code and the output.</returns>
    /// <exception cref="Win32Exception">The program is not there.</exception>
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);

    /// <summary>
    /// Runs a program as administrator on Windows (the system asks the user first) and waits for it. The output is
    /// not available.
    /// </summary>
    /// <param name="fileName">The program.</param>
    /// <param name="arguments">The arguments, each passed as it is.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The exit code.</returns>
    /// <exception cref="ElevationCancelledException">The user said no.</exception>
    Task<int> RunAsAdministratorAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}
