using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Golether.Localization;
using Golether.Tunnels.AmneziaWG.Configuration;

namespace Golether.Tunnels.AmneziaWG.Control;

/// <summary>
/// The short-lived elevated mode of the application: brings one tunnel up or down and exits.
/// </summary>
/// <remarks>
/// <para>
/// On Windows the application does not need to run as administrator. For each tunnel change it starts its own
/// executable with <see cref="SWITCH"/> through the UAC prompt; that process only calls <c>amneziawg.exe</c> for a
/// configuration of the tunnels folder and ends. No privileged process of the application stays behind; the tunnel
/// service it leaves is made of copies that only administrators can change (<see cref="ITunnelEngineGuard"/>), never
/// of files of the data folder, which every program of the user can write.
/// </para>
/// <para>Arguments: <c>--tunnel-helper up &lt;config.conf&gt; &lt;result file&gt;</c> or
/// <c>--tunnel-helper down &lt;config.conf&gt; &lt;result file&gt;</c>. The result file receives the error text.</para>
/// </remarks>
public static class TunnelHelper
{
    /// <summary>
    /// The command-line switch of the helper mode.
    /// </summary>
    public const string SWITCH = "--tunnel-helper";

    /// <summary>
    /// The verb that brings a tunnel up.
    /// </summary>
    public const string UP_VERB = "up";

    /// <summary>
    /// The verb that brings a tunnel down.
    /// </summary>
    public const string DOWN_VERB = "down";

    /// <summary>
    /// The extension of result files.
    /// </summary>
    public const string RESULT_EXTENSION = ".result";

    /// <summary>
    /// Exit code: done.
    /// </summary>
    public const int SUCCESS = 0;

    /// <summary>
    /// Exit code: AmneziaWG failed; the reason is in the result file.
    /// </summary>
    public const int FAILURE = 1;

    /// <summary>
    /// Exit code: the arguments were rejected.
    /// </summary>
    public const int INVALID_ARGUMENTS = 2;

    /// <summary>
    /// The time one AmneziaWG call may take.
    /// </summary>
    private static readonly TimeSpan _callTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Checks whether the command line starts the helper mode.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns><see langword="true"/> for the helper mode.</returns>
    public static bool IsHelperInvocation(IReadOnlyList<string> args)
        => args is { Count: > 0 } && args[0] == SWITCH;

    /// <summary>
    /// The beginning of a result that says a file of the engine is not the known one; the name of the file follows.
    /// </summary>
    private const string ENGINE_CHANGED_PREFIX = "engine-changed\t";

    /// <summary>
    /// A result that says the configuration is not one the application writes.
    /// </summary>
    private const string CONFIGURATION_REJECTED = "configuration-rejected";

    /// <summary>
    /// Runs the helper mode.
    /// </summary>
    /// <param name="args">The command-line arguments including <see cref="SWITCH"/>.</param>
    /// <param name="runner">Runs AmneziaWG.</param>
    /// <param name="windowsExecutable">The path of <c>amneziawg.exe</c> the application found.</param>
    /// <param name="guard">Gives the engine and the configuration that no program without administrator rights can
    /// change.</param>
    /// <returns>The exit code.</returns>
    public static int Run(IReadOnlyList<string> args, IProcessRunner runner, string windowsExecutable, ITunnelEngineGuard guard)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(guard);
        if (!TryParse(args, out var verb, out var configPath, out var resultPath))
        {
            return INVALID_ARGUMENTS;
        }

        try
        {
            Execute(verb, configPath, runner, windowsExecutable, guard);
            return SUCCESS;
        }
        catch (Exception ex) when (ex is TunnelControlException or Win32Exception or TimeoutException or InvalidOperationException)
        {
            TryWrite(resultPath, ex.Message);
            return FAILURE;
        }
    }

    /// <summary>
    /// Brings a tunnel up or down with administrator rights: in the helper, or in the application when it runs with
    /// those rights itself. The service is made of the protected copies only.
    /// </summary>
    /// <param name="verb"><see cref="UP_VERB"/> or <see cref="DOWN_VERB"/>.</param>
    /// <param name="configPath">The configuration file the application wrote.</param>
    /// <param name="runner">Runs AmneziaWG.</param>
    /// <param name="windowsExecutable">The path of <c>amneziawg.exe</c> the application found.</param>
    /// <param name="guard">Gives the protected copies.</param>
    /// <exception cref="TunnelControlException">A step failed.</exception>
    public static void Execute(string verb, string configPath, IProcessRunner runner, string windowsExecutable, ITunnelEngineGuard guard)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(guard);

        var interfaceName = Path.GetFileNameWithoutExtension(configPath);
        TunnelNames.Validate(interfaceName);
        var engine = guard.PrepareEngine(windowsExecutable);

        if (verb == UP_VERB)
        {
            // Replace an existing service so a changed configuration takes effect.
            var protectedConfig = guard.PrepareConfiguration(interfaceName, configPath);
            Call(runner, engine, ["/uninstalltunnelservice", interfaceName], ignoreFailure: true);
            Call(runner, engine, ["/installtunnelservice", protectedConfig], ignoreFailure: false);
        }
        else
        {
            Call(runner, engine, ["/uninstalltunnelservice", interfaceName], ignoreFailure: false);
            guard.ForgetConfiguration(interfaceName);
        }
    }

    /// <summary>
    /// Encodes the fact that a file of the engine is not the known one, for the application to word.
    /// </summary>
    /// <param name="file">The name of the file.</param>
    /// <returns>The language-neutral result.</returns>
    public static string EncodeEngineChanged(string file) => ENGINE_CHANGED_PREFIX + file;

    /// <summary>
    /// Encodes the fact that the configuration is not one the application writes.
    /// </summary>
    /// <returns>The language-neutral result.</returns>
    public static string EncodeConfigurationRejected() => CONFIGURATION_REJECTED;

    /// <summary>
    /// Builds the helper arguments for a tunnel change.
    /// </summary>
    /// <param name="verb"><see cref="UP_VERB"/> or <see cref="DOWN_VERB"/>.</param>
    /// <param name="configPath">The configuration file.</param>
    /// <param name="resultPath">The result file.</param>
    /// <returns>The arguments.</returns>
    public static string[] BuildArguments(string verb, string configPath, string resultPath)
        => [SWITCH, verb, configPath, resultPath];

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
        if (args is not { Count: 4 } || args[0] != SWITCH || args[1] is not (UP_VERB or DOWN_VERB))
        {
            return false;
        }

        var config = args[2];
        var result = args[3];
        if (!Path.IsPathFullyQualified(config) || !Path.IsPathFullyQualified(result) ||
            !string.Equals(Path.GetExtension(config), ".conf", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(result), RESULT_EXTENSION, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(Path.GetFullPath(config)), Path.GetDirectoryName(Path.GetFullPath(result)), StringComparison.OrdinalIgnoreCase) ||
            (args[1] == UP_VERB && !File.Exists(config)))
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
            result = runner.RunAsync(executable, arguments, _callTimeout, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ignoreFailure && ex is Win32Exception or TimeoutException or InvalidOperationException)
        {
            return;
        }

        if (!result.Succeeded && !ignoreFailure)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            throw new TunnelControlException(EncodeToolFailure(Path.GetFileName(executable), result.ExitCode, detail.Trim()));
        }
    }

    /// <summary>
    /// The marker that starts a tool failure in the result file. The helper runs in its own process that knows nothing
    /// about the language the user chose, so it reports the facts and the application words them.
    /// </summary>
    private const string TOOL_FAILURE_PREFIX = "tool-failed\t";

    /// <summary>
    /// Encodes the failure of AmneziaWG for the result file.
    /// </summary>
    /// <param name="tool">The program name.</param>
    /// <param name="exitCode">The exit code.</param>
    /// <param name="detail">What the program printed.</param>
    /// <returns>The text for the result file.</returns>
    public static string EncodeToolFailure(string tool, int exitCode, string detail)
        => string.Concat(TOOL_FAILURE_PREFIX, tool, "\t", exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture), "\t", detail);

    /// <summary>
    /// Words the text the helper left in the result file in the language of the application.
    /// </summary>
    /// <param name="text">The content of the result file.</param>
    /// <returns>The message for the user; a text that is not an encoded failure is returned as is.</returns>
    public static string DescribeFailure(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.StartsWith(TOOL_FAILURE_PREFIX, StringComparison.Ordinal) &&
            text[TOOL_FAILURE_PREFIX.Length..].Split('\t', 3) is [var tool, var code, var detail])
        {
            return Texts.Format("Tunnel.Error.ToolFailed", tool, code, detail);
        }

        if (text.StartsWith(ENGINE_CHANGED_PREFIX, StringComparison.Ordinal))
        {
            return Texts.Format("Tunnel.Error.EngineChanged", text[ENGINE_CHANGED_PREFIX.Length..]);
        }

        return text == CONFIGURATION_REJECTED ? Texts.Get("Tunnel.Error.ConfigurationRejected") : text;
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
            // The folder belongs to the user and the helper has administrator rights: a folder replaced by a link, or a
            // file put there beforehand, would make it write somewhere else. Only a new file in a real folder is written.
            if (new DirectoryInfo(Path.GetDirectoryName(path)!).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                Trace.WriteLine($"Golether tunnel helper: {text}");
                return;
            }

            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(Encoding.UTF8.GetBytes(text));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Golether tunnel helper: {text}");
        }
    }
}
