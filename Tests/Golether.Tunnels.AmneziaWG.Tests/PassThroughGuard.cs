using Golether.Tunnels.AmneziaWG.Control;

namespace Golether.Tunnels.AmneziaWG.Tests;

/// <summary>
/// A guard that gives the files as they are, for the tests of the steps around it; <see cref="ProtectedTunnelFilesTests"/>
/// tests the real one.
/// </summary>
internal sealed class PassThroughGuard : ITunnelEngineGuard
{
    /// <summary>
    /// Gets the tunnels whose configuration was forgotten.
    /// </summary>
    public List<string> Forgotten { get; } = [];

    /// <inheritdoc />
    public string PrepareEngine(string executable) => executable;

    /// <inheritdoc />
    public string PrepareConfiguration(string interfaceName, string configuration) => configuration;

    /// <inheritdoc />
    public void ForgetConfiguration(string interfaceName) => Forgotten.Add(interfaceName);
}
