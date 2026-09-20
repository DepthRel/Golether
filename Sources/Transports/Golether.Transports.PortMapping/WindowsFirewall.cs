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
    public const string Switch = "--firewall-helper";

    /// <summary>
    /// The verb that allows incoming connections.
    /// </summary>
    public const string AllowVerb = "allow";

    /// <summary>
    /// The verb that removes the rule.
    /// </summary>
    public const string RemoveVerb = "remove";

    /// <summary>
    /// The name of the rule.
    /// </summary>
    public const string RuleName = "Golether";

    /// <summary>
    /// Exit code: done.
    /// </summary>
    public const int Success = 0;

    /// <summary>
    /// Exit code: the firewall refused the change.
    /// </summary>
    public const int Failure = 1;

    /// <summary>
    /// Exit code: the arguments were rejected.
    /// </summary>
    public const int InvalidArguments = 2;

    /// <summary>
    /// <c>NET_FW_RULE_DIR_IN</c>.
    /// </summary>
    private const int DirectionIn = 1;

    /// <summary>
    /// <c>NET_FW_ACTION_ALLOW</c>.
    /// </summary>
    private const int ActionAllow = 1;

    /// <summary>
    /// The time one <c>netsh</c> call may take.
    /// </summary>
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);

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
    /// <param name="verb"><see cref="AllowVerb"/> or <see cref="RemoveVerb"/>.</param>
    /// <returns>The arguments.</returns>
    public static string[] BuildArguments(string verb) => [Switch, verb];

    /// <summary>
    /// Checks whether the command line starts the helper mode.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns><see langword="true"/> for the helper mode.</returns>
    public static bool IsHelperInvocation(IReadOnlyList<string> args)
        => args is { Count: > 0 } && args[0] == Switch;

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
        if (args is not { Count: 2 } || args[0] != Switch || args[1] is not (AllowVerb or RemoveVerb))
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
            return InvalidArguments;
        }

        try
        {
            // The old rule goes first either way, so repeated calls do not pile up duplicates.
            Netsh(["advfirewall", "firewall", "delete", "rule", $"name={RuleName}", "dir=in"], ignoreFailure: true);
            if (verb == RemoveVerb)
            {
                return Success;
            }

            Netsh(
                [
                    "advfirewall", "firewall", "add", "rule", $"name={RuleName}", "dir=in", "action=allow",
                    $"program={program}", "enable=yes", "profile=any",
                    $"description=Golether: приём подключений участников сеанса",
                ],
                ignoreFailure: false);
            return Success;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Trace.WriteLine($"Golether firewall helper: {ex.Message}");
            return Failure;
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
            if (Member(rule, "ApplicationName") is not string application
                || !string.Equals(Path.GetFullPath(application), program, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Member(rule, "Enabled") is true
                && Convert.ToInt32(Member(rule, "Direction"), CultureInfo.InvariantCulture) == DirectionIn
                && Convert.ToInt32(Member(rule, "Action"), CultureInfo.InvariantCulture) == ActionAllow)
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
        var info = new ProcessStartInfo("netsh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("netsh did not start.");
        process.WaitForExit(CallTimeout);
        if (!ignoreFailure && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"netsh finished with code {process.ExitCode}.");
        }
    }
}
