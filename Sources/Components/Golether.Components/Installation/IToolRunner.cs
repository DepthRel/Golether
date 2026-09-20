namespace Golether.Components.Installation;

/// <summary>
/// Runs external tools (the Windows tar and Inno Setup installers).
/// </summary>
public interface IToolRunner
{
    /// <summary>
    /// Runs a program without a window and waits for it.
    /// </summary>
    /// <param name="fileName">The program.</param>
    /// <param name="arguments">The arguments, passed one by one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The exit code.</returns>
    Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}
