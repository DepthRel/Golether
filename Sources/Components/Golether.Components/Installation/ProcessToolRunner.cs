using System.Diagnostics;
using Golether.Localization;

namespace Golether.Components.Installation;

/// <summary>
/// <see cref="IToolRunner"/> based on <see cref="Process"/>.
/// </summary>
public sealed class ProcessToolRunner : IToolRunner
{
    /// <inheritdoc />
    public async Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(fileName) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException(Texts.Format("Install.Error.ProcessNotStarted", fileName));
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return process.ExitCode;
    }
}
