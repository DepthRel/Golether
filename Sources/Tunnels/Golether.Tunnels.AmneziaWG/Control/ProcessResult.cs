namespace Golether.Tunnels.AmneziaWG.Control;

/// <summary>
/// The result of an external process.
/// </summary>
/// <param name="ExitCode">The exit code.</param>
/// <param name="StandardOutput">The captured standard output.</param>
/// <param name="StandardError">The captured standard error.</param>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>
    /// Gets a value indicating whether the process succeeded.
    /// </summary>
    public bool Succeeded => ExitCode == 0;
}
