using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Golether.Web.Server.UI.Services;

/// <summary>
/// What a finished program left behind.
/// </summary>
/// <param name="ExitCode">The exit code.</param>
/// <param name="Output">What it wrote, the standard output and the errors together.</param>
public sealed record ProcessResult(int ExitCode, string Output);

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

/// <summary>
/// The user declined to give the rights the action needs.
/// </summary>
public sealed class ElevationCancelledException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ElevationCancelledException"/> class.
    /// </summary>
    public ElevationCancelledException()
        : base("The user declined to run the action as administrator.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ElevationCancelledException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public ElevationCancelledException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ElevationCancelledException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="inner">The cause.</param>
    public ElevationCancelledException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Runs programs of the system.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    /// <summary>
    /// The error of Windows when the user answers "No" to the question of the system.
    /// </summary>
    private const int ErrorCancelled = 1223;

    /// <summary>
    /// How long a tool may run: starting or stopping a service takes seconds, a question to the user takes longer.
    /// </summary>
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(2);

    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new Win32Exception($"{fileName} did not start.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(Limit);
        var output = process.StandardOutput.ReadToEndAsync(limit.Token);
        var errors = process.StandardError.ReadToEndAsync(limit.Token);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return new ProcessResult(process.ExitCode, (await output.ConfigureAwait(false)) + (await errors.ConfigureAwait(false)));
    }

    /// <inheritdoc />
    public async Task<int> RunAsAdministratorAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(fileName, string.Join(' ', arguments.Select(Quote)))
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            throw new ElevationCancelledException(ex.Message, ex);
        }

        using (process)
        {
            if (process is null)
            {
                throw new Win32Exception($"{fileName} did not start.");
            }

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(Limit);
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            return process.ExitCode;
        }
    }

    /// <summary>
    /// Quotes one argument of a Windows command line so that the program receives it unchanged.
    /// </summary>
    /// <param name="argument">The argument.</param>
    /// <returns>The quoted argument.</returns>
    public static string Quote(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0)
        {
            return argument;
        }

        var quoted = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            // Backslashes before a quote are doubled, the quote itself escaped; elsewhere they stay as they are.
            quoted.Append('\\', c == '"' ? (backslashes * 2) + 1 : backslashes);
            backslashes = 0;
            quoted.Append(c);
        }

        quoted.Append('\\', backslashes * 2);
        return quoted.Append('"').ToString();
    }
}
