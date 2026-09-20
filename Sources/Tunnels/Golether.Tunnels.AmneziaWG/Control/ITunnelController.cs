using Golether.Tunnels.AmneziaWG.Configuration;

namespace Golether.Tunnels.AmneziaWG.Control;

/// <summary>
/// Brings AmneziaWG tunnels up and down.
/// </summary>
public interface ITunnelController
{
    /// <summary>
    /// Checks whether the AmneziaWG tools are installed.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="null"/> when available, otherwise the reason.</returns>
    Task<string?> CheckAvailabilityAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes the configuration and brings the tunnel up.
    /// </summary>
    /// <param name="interfaceName">The interface name.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the tunnel is up.</returns>
    /// <exception cref="TunnelControlException">The tools reported an error.</exception>
    Task UpAsync(string interfaceName, AwgConfiguration configuration, CancellationToken cancellationToken);

    /// <summary>
    /// Brings the tunnel down.
    /// </summary>
    /// <param name="interfaceName">The interface name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the tunnel is down.</returns>
    /// <exception cref="TunnelControlException">The tools reported an error.</exception>
    Task DownAsync(string interfaceName, CancellationToken cancellationToken);
}
