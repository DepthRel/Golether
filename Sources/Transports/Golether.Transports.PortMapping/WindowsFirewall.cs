using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Golether.Core.Data.Enums;

namespace Golether.Transports.PortMapping;

/// <summary>
/// The inbound rule of Golether in the Windows firewall.
/// </summary>
/// <remarks>
/// <para>
/// Windows treats a fresh tunnel adapter as a public network and blocks incoming connections on it, so a session
/// over AmneziaWG or WireGuard fails even though the two computers can reach each other. One rule for the program
/// fixes it for every port the application uses, on every profile.
/// </para>
/// <para>
/// Reading the rules needs no rights and is done through the firewall COM object. Writing needs administrator
/// rights, so it goes through the short-lived elevated helper, like tunnel changes do.
/// </para>
/// </remarks>
public static class WindowsFirewall
{
    /// <summary>
    /// The command-line switch of the helper mode.
    /// </summary>
    public const string SWITCH = "--firewall-helper";

    /// <summary>
    /// The verb that allows incoming connections.
    /// </summary>
    public const string ALLOW_VERB = "allow";

    /// <summary>
    /// The verb that removes the rule.
    /// </summary>
    public const string REMOVE_VERB = "remove";

    /// <summary>
    /// The name of the rule.
    /// </summary>
    public const string RULE_NAME = "Golether";

    /// <summary>
    /// Exit code: done.
    /// </summary>
    public const int SUCCESS = 0;

    /// <summary>
    /// Exit code: the firewall refused the change.
    /// </summary>
    public const int FAILURE = 1;

    /// <summary>
    /// Exit code: the arguments were rejected.
    /// </summary>
    public const int INVALID_ARGUMENTS = 2;

    /// <summary>
    /// <c>NET_FW_RULE_DIR_IN</c>.
    /// </summary>
    private const int DIRECTION_IN = 1;

    /// <summary>
    /// <c>NET_FW_ACTION_ALLOW</c>.
    /// </summary>
    private const int ACTION_ALLOW = 1;

    /// <summary>
    /// The time one <c>netsh</c> call may take.
    /// </summary>
    private static readonly TimeSpan _callTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Checks whether incoming connections to a program are allowed.
    /// </summary>
    /// <param name="programPath">The program; the running one when <see langword="null"/>.</param>
    /// <returns>The state of the rule.</returns>
    public static FirewallState Check(string? programPath = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            // Other systems do not block a listening program by default.
            return FirewallState.Allowed;
        }

        var program = programPath ?? CurrentProgram();
        if (program is null)
        {
            return FirewallState.Unknown;
        }

        try
        {
            return CheckWindows(program);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Trace.WriteLine($"Golether firewall: {ex.Message}");
            return FirewallState.Unknown;
        }
    }

    /// <summary>
    /// Builds the helper arguments.
    /// </summary>
    /// <param name="verb"><see cref="ALLOW_VERB"/> or <see cref="REMOVE_VERB"/>.</param>
    /// <returns>The arguments.</returns>
    public static string[] BuildArguments(string verb) => [SWITCH, verb];

    /// <summary>
    /// Checks whether the command line starts the helper mode.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns><see langword="true"/> for the helper mode.</returns>
    public static bool IsHelperInvocation(IReadOnlyList<string> args)
        => args is { Count: > 0 } && args[0] == SWITCH;

    /// <summary>
    /// Validates the helper arguments. The helper runs elevated, so it takes no paths from the command line at all:
    /// the rule always points at the running executable.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <param name="verb">The verb.</param>
    /// <returns><see langword="true"/> when the arguments are valid.</returns>
    public static bool TryParse(IReadOnlyList<string> args, out string verb)
    {
        verb = string.Empty;
        if (args is not { Count: 2 } || args[0] != SWITCH || args[1] is not (ALLOW_VERB or REMOVE_VERB))
        {
            return false;
        }

        verb = args[1];
        return true;
    }

    /// <summary>
    /// Runs the elevated helper: adds or removes the rule for the running executable.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The exit code.</returns>
    public static int Run(IReadOnlyList<string> args)
    {
        if (!TryParse(args, out var verb) || CurrentProgram() is not { } program)
        {
            return INVALID_ARGUMENTS;
        }

        try
        {
            // The old rule goes first either way, so repeated calls do not pile up duplicates.
            Netsh(["advfirewall", "firewall", "delete", "rule", $"name={RULE_NAME}", "dir=in"], ignoreFailure: true);
            if (verb == REMOVE_VERB)
            {
                return SUCCESS;
            }

            Netsh(
                [
                    "advfirewall", "firewall", "add", "rule", $"name={RULE_NAME}", "dir=in", "action=allow",
                    $"program={program}", "enable=yes", "profile=any",
                    "description=Golether: incoming connections of session participants",
                ],
                ignoreFailure: false);
            return SUCCESS;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Trace.WriteLine($"Golether firewall helper: {ex.Message}");
            return FAILURE;
        }
    }

    /// <summary>
    /// Returns the path of the running executable, or <see langword="null"/> when it cannot be determined.
    /// </summary>
    /// <returns>The full path.</returns>
    public static string? CurrentProgram()
    {
        try
        {
            var path = Environment.ProcessPath;
            return string.IsNullOrEmpty(path) ? null : Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Looks for an enabled allow rule of this program among the inbound rules.
    /// </summary>
    /// <param name="program">The full path of the program.</param>
    /// <returns>The state of the rule.</returns>
    [SupportedOSPlatform("windows")]
    private static FirewallState CheckWindows(string program)
    {
        var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
        if (type is null || Activator.CreateInstance(type) is not { } policy)
        {
            return FirewallState.Unknown;
        }

        if (policy.GetType().InvokeMember("Rules", System.Reflection.BindingFlags.GetProperty, null, policy, null) is not System.Collections.IEnumerable rules)
        {
            return FirewallState.Unknown;
        }

        foreach (var rule in rules)
        {
            if (Member(rule, "ApplicationName") is not string { Length: > 0 } application ||
                !string.Equals(Path.GetFullPath(application), program, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Member(rule, "Enabled") is true &&
                Convert.ToInt32(Member(rule, "Direction"), CultureInfo.InvariantCulture) == DIRECTION_IN &&
                Convert.ToInt32(Member(rule, "Action"), CultureInfo.InvariantCulture) == ACTION_ALLOW)
            {
                return FirewallState.Allowed;
            }
        }

        return FirewallState.Missing;
    }

    /// <summary>
    /// Reads a property of a firewall rule, or <see langword="null"/> when it is not available.
    /// </summary>
    /// <param name="rule">The rule.</param>
    /// <param name="name">The property.</param>
    /// <returns>The value.</returns>
    private static object? Member(object rule, string name)
    {
        try
        {
            return rule.GetType().InvokeMember(name, System.Reflection.BindingFlags.GetProperty, null, rule, null);
        }
        catch (Exception ex) when (ex is MissingMemberException or System.Reflection.TargetInvocationException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Calls <c>netsh</c> and checks the exit code.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="ignoreFailure">Whether a non-zero exit code is acceptable (there was nothing to delete).</param>
    /// <exception cref="InvalidOperationException">The call failed.</exception>
    private static void Netsh(IReadOnlyList<string> arguments, bool ignoreFailure)
    {
        // The helper runs with administrator rights, so the program is named by its full path in the system
        // folder: a bare name is looked for in the current folder first, where anybody may have put a "netsh.exe".
        // The output is not captured: nobody reads it, and a full pipe would only make the program wait.
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("netsh did not start.");
        if (!process.WaitForExit(_callTimeout))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("netsh did not finish in time.");
        }

        if (!ignoreFailure && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"netsh finished with code {process.ExitCode}.");
        }
    }
}
