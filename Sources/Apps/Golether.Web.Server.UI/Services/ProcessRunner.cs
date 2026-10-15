using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Golether.Web.Server.UI.Services;

/// <summary>
/// Runs programs of the system.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    /// <summary>
    /// The error of Windows when the user answers "No" to the question of the system.
    /// </summary>
    private const int ERROR_CANCELLED = 1223;

    /// <summary>
    /// How long a tool may run: starting or stopping a service takes seconds, a question to the user takes longer.
    /// </summary>
    private static readonly TimeSpan _limit = TimeSpan.FromMinutes(2);

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
        limit.CancelAfter(_limit);
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
        catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
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
            limit.CancelAfter(_limit);
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
