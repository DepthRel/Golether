namespace Golether.Web.Server.UI.Services;

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
