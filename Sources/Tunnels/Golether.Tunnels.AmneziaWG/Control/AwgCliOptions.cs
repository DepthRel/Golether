namespace Golether.Tunnels.AmneziaWG.Control;

/// <summary>
/// Settings of <see cref="AwgCliTunnelController"/>.
/// </summary>
public sealed record AwgCliOptions
{
    /// <summary>
    /// Gets the directory of configuration files.
    /// </summary>
    public required string ConfigDirectory { get; init; }

    /// <summary>
    /// Gets the AmneziaWG for Windows executable.
    /// </summary>
    public string WindowsExecutable { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AmneziaWG", "amneziawg.exe");

    /// <summary>
    /// Gets a lookup of the AmneziaWG the application carries itself, asked before every call. The component can be
    /// installed while the application is running, so the path must not be decided once at startup.
    /// </summary>
    public Func<string?>? FindCarriedExecutable { get; init; }

    /// <summary>
    /// Returns the AmneziaWG to call: the copy the application carries when it is there, otherwise the one installed
    /// in the system.
    /// </summary>
    /// <returns>The path.</returns>
    public string ResolveWindowsExecutable() => FindCarriedExecutable?.Invoke() ?? WindowsExecutable;

    /// <summary>
    /// Gets the <c>awg-quick</c> executable on Linux and macOS.
    /// </summary>
    public string QuickExecutable { get; init; } = "awg-quick";

    /// <summary>
    /// Gets an optional elevation program for Linux (for example <c>pkexec</c>); <see langword="null"/> runs the tools
    /// directly.
    /// </summary>
    public string? ElevationCommand { get; init; }

    /// <summary>
    /// Gets a value indicating whether <c>awg-quick</c> runs through the macOS administrator prompt
    /// (<c>osascript … with administrator privileges</c>).
    /// </summary>
    public bool UseAppleScriptElevation { get; init; }

    /// <summary>
    /// Gets the executable started with <see cref="TunnelHelper.Switch"/> through the UAC prompt on Windows, or
    /// <see langword="null"/> to call AmneziaWG directly (the application already runs as administrator).
    /// </summary>
    public string? HelperExecutable { get; init; }

    /// <summary>
    /// Gets the timeout of one tool invocation.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
}
