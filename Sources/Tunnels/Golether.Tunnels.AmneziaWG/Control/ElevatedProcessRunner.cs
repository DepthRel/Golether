using System.Diagnostics;

namespace Golether.Tunnels.AmneziaWG.Control;

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
