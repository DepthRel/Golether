namespace Golether.Web.Server.UI.Services;

/// <summary>
/// What a finished program left behind.
/// </summary>
/// <param name="ExitCode">The exit code.</param>
/// <param name="Output">What it wrote, the standard output and the errors together.</param>
public sealed record ProcessResult(int ExitCode, string Output);
