namespace Golether.Tunnels.AmneziaWG.Control;

/// <summary>
/// Gives a process with administrator rights only files that a process without them cannot change: the engine and the
/// configuration a tunnel service is made of.
/// </summary>
/// <remarks>
/// The service of a tunnel runs as SYSTEM and starts the engine and reads the configuration again every time it
/// starts. Taken from the data folder of the user, either could be swapped by any program of that user, and the next
/// start would run it with full rights, without asking anybody.
/// </remarks>
public interface ITunnelEngineGuard
{
    /// <summary>
    /// Returns an engine to start with administrator rights: the given one when it lies where only administrators can
    /// write, otherwise a copy in such a place, checked file by file against the known hashes.
    /// </summary>
    /// <param name="executable">The engine found for the application.</param>
    /// <returns>The path of the engine to start.</returns>
    /// <exception cref="TunnelControlException">A file differs from the known one, or the copy could not be made.</exception>
    string PrepareEngine(string executable);

    /// <summary>
    /// Reads a configuration written by the application, keeps only what it knows and writes it anew where only
    /// administrators and the system can read and change it (it holds a private key).
    /// </summary>
    /// <param name="interfaceName">The name of the tunnel.</param>
    /// <param name="configuration">The configuration file of the application.</param>
    /// <returns>The path of the protected configuration.</returns>
    /// <exception cref="TunnelControlException">The configuration is not one the application writes.</exception>
    string PrepareConfiguration(string interfaceName, string configuration);

    /// <summary>
    /// Removes the protected configuration of a tunnel that was brought down, and the protected engine when no tunnel
    /// is left.
    /// </summary>
    /// <param name="interfaceName">The name of the tunnel.</param>
    void ForgetConfiguration(string interfaceName);
}
