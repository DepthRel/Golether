using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Golether.Tunnels.AmneziaWG.Configuration;

namespace Golether.Tunnels.AmneziaWG.Control;

/// <summary>
/// The short-lived elevated mode of the application: brings one tunnel up or down and exits.
/// </summary>
/// <remarks>
/// <para>
/// On Windows the application does not need to run as administrator. For each tunnel change it starts its own
/// executable with <see cref="Switch"/> through the UAC prompt; that process only calls <c>amneziawg.exe</c> for a
/// configuration of the tunnels folder and ends. No service or privileged process stays behind.
/// </para>
/// <para>Arguments: <c>--tunnel-helper up &lt;config.conf&gt; &lt;result file&gt;</c> or
/// <c>--tunnel-helper down &lt;config.conf&gt; &lt;result file&gt;</c>. The result file receives the error text.</para>
/// </remarks>
public static class TunnelHelper
{
    /// <summary>
    /// The command-line switch of the helper mode.
    /// </summary>
    public const string Switch = "--tunnel-helper";

    /// <summary>
    /// The verb that brings a tunnel up.
    /// </summary>
    public const string UpVerb = "up";

    /// <summary>
    /// The verb that brings a tunnel down.
    /// </summary>
    public const string DownVerb = "down";

    /// <summary>
    /// The extension of result files.
    /// </summary>
    public const string ResultExtension = ".result";

    /// <summary>
    /// Exit code: done.
    /// </summary>
    public const int Success = 0;

    /// <summary>
    /// Exit code: AmneziaWG failed; the reason is in the result file.
    /// </summary>
    public const int Failure = 1;

    /// <summary>
    /// Exit code: the arguments were rejected.
    /// </summary>
    public const int InvalidArguments = 2;

    /// <summary>
    /// The time one AmneziaWG call may take.
    /// </summary>
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Checks whether the command line starts the helper mode.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns><see langword="true"/> for the helper mode.</returns>
    public static bool IsHelperInvocation(IReadOnlyList<string> args)
        => args is { Count: > 0 } && args[0] == Switch;

    /// <summary>
    /// Runs the helper mode.
    /// </summary>
    /// <param name="args">The command-line arguments including <see cref="Switch"/>.</param>
    /// <param name="runner">Runs AmneziaWG.</param>
    /// <param name="windowsExecutable">The path of <c>amneziawg.exe</c>.</param>
    /// <returns>The exit code.</returns>
    public static int Run(IReadOnlyList<string> args, IProcessRunner runner, string windowsExecutable)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(runner);
        if (!TryParse(args, out var verb, out var configPath, out var resultPath))
        {
            return InvalidArguments;
        }

        var interfaceName = Path.GetFileNameWithoutExtension(configPath);
        try
        {
            if (verb == UpVerb)
            {
                // Replace an existing service so a changed configuration takes effect.
                Call(runner, windowsExecutable, ["/uninstalltunnelservice", interfaceName], ignoreFailure: true);
                Call(runner, windowsExecutable, ["/installtunnelservice", configPath], ignoreFailure: false);
            }
            else
            {
                Call(runner, windowsExecutable, ["/uninstalltunnelservice", interfaceName], ignoreFailure: false);
            }

            return Success;
        }
        catch (Exception ex) when (ex is TunnelControlException or Win32Exception or TimeoutException or InvalidOperationException)
        {
            TryWrite(resultPath, ex.Message);
            return Failure;
        }
    }

    /// <summary>
    /// Builds the helper arguments for a tunnel change.
    /// </summary>
    /// <param name="verb"><see cref="UpVerb"/> or <see cref="DownVerb"/>.</param>
    /// <param name="configPath">The configuration file.</param>
    /// <param name="resultPath">The result file.</param>
    /// <returns>The arguments.</returns>
    public static string[] BuildArguments(string verb, string configPath, string resultPath)
        => [Switch, verb, configPath, resultPath];

    /// <summary>
    /// Validates the helper arguments. The helper runs elevated, so it accepts only a <c>.conf</c> file with a valid
    /// interface name and a result file next to it.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <param name="verb">The verb.</param>
    /// <param name="configPath">The configuration file.</param>
    /// <param name="resultPath">The result file.</param>
    /// <returns><see langword="true"/> when the arguments are valid.</returns>
    public static bool TryParse(IReadOnlyList<string> args, out string verb, out string configPath, out string resultPath)
    {
        verb = configPath = resultPath = string.Empty;
        if (args is not { Count: 4 } || args[0] != Switch || args[1] is not (UpVerb or DownVerb))
        {
            return false;
        }

        var config = args[2];
        var result = args[3];
        if (!Path.IsPathFullyQualified(config) || !Path.IsPathFullyQualified(result)
            || !string.Equals(Path.GetExtension(config), ".conf", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetExtension(result), ResultExtension, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(config)), Path.GetDirectoryName(Path.GetFullPath(result)), StringComparison.OrdinalIgnoreCase)
            || (args[1] == UpVerb && !File.Exists(config)))
        {
            return false;
        }

        try
        {
            TunnelNames.Validate(Path.GetFileNameWithoutExtension(config));
        }
        catch (ArgumentException)
        {
            return false;
        }

        verb = args[1];
        configPath = Path.GetFullPath(config);
        resultPath = Path.GetFullPath(result);
        return true;
    }

    /// <summary>
    /// Calls AmneziaWG and converts failures.
    /// </summary>
    /// <param name="runner">The runner.</param>
    /// <param name="executable">The program.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="ignoreFailure">Whether a non-zero exit code is acceptable.</param>
    /// <exception cref="TunnelControlException">AmneziaWG failed.</exception>
    private static void Call(IProcessRunner runner, string executable, IReadOnlyList<string> arguments, bool ignoreFailure)
    {
        ProcessResult result;
        try
        {
            result = runner.RunAsync(executable, arguments, CallTimeout, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ignoreFailure && ex is Win32Exception or TimeoutException or InvalidOperationException)
        {
            return;
        }

        if (!result.Succeeded && !ignoreFailure)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            throw new TunnelControlException($"{Path.GetFileName(executable)} завершился с кодом {result.ExitCode}: {detail.Trim()}");
        }
    }

    /// <summary>
    /// Writes the error text for the calling application.
    /// </summary>
    /// <param name="path">The result file.</param>
    /// <param name="text">The text.</param>
    private static void TryWrite(string path, string text)
    {
        try
        {
            File.WriteAllText(path, text, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Golether tunnel helper: {text}");
        }
    }
}

/// <summary>
/// Starts a program with administrator rights through the UAC prompt (Windows). Output is not captured.
/// </summary>
public sealed class ElevatedProcessRunner : IProcessRunner
{
    /// <summary>
    /// <c>ERROR_CANCELLED</c>: the user declined the UAC prompt.
    /// </summary>
    public const int Cancelled = 1223;

    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"'{fileName}' did not start.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"'{Path.GetFileName(fileName)}' did not finish in {timeout.TotalSeconds:0} s.");
        }

        return new ProcessResult(process.ExitCode, string.Empty, string.Empty);
    }
}
