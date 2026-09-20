namespace Golether.Tunnels.AmneziaWG.Control;

/// <summary>
/// Runs external programs without a shell.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs a program and waits for it.
    /// </summary>
    /// <param name="fileName">The program.</param>
    /// <param name="arguments">The arguments, passed one by one without shell interpretation.</param>
    /// <param name="timeout">The maximum run time.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result.</returns>
    /// <exception cref="TimeoutException">The program did not finish in time and was killed.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">The program cannot be started.</exception>
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken);
}
