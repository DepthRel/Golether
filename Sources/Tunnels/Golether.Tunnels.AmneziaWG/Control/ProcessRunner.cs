using System.Diagnostics;
using System.Text;

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

/// <summary>
/// <see cref="IProcessRunner"/> based on <see cref="Process"/>.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    /// <summary>
    /// The maximum captured output per stream.
    /// </summary>
    private const int MaxOutput = 64 * 1024;

    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        var info = new ProcessStartInfo(fileName)
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
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"'{fileName}' did not start.");
        var output = ReadLimitedAsync(process.StandardOutput);
        var error = ReadLimitedAsync(process.StandardError);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException($"'{Path.GetFileName(fileName)}' did not finish in {timeout.TotalSeconds:0} s.");
        }

        return new ProcessResult(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }

    /// <summary>
    /// Reads a stream up to <see cref="MaxOutput"/> characters and discards the rest.
    /// </summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The captured text.</returns>
    private static async Task<string> ReadLimitedAsync(StreamReader reader)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            if (text.Length < MaxOutput)
            {
                text.Append(buffer, 0, Math.Min(read, MaxOutput - text.Length));
            }
        }

        return text.ToString();
    }
}
