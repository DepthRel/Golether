namespace Golether.Transports.PortMapping;

/// <summary>
/// Opens and closes a port on one kind of router interface.
/// </summary>
public interface IPortMappingProtocol
{
    /// <summary>
    /// Gets the name of the protocol.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Opens a TCP port.
    /// </summary>
    /// <param name="port">The port on this device, also requested on the router.</param>
    /// <param name="lifetime">The requested lease.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The mapping, or <see langword="null"/> when the router does not answer or refuses.</returns>
    Task<PortMappingResult?> MapAsync(int port, TimeSpan lifetime, CancellationToken cancellationToken);

    /// <summary>
    /// Closes a mapping opened by <see cref="MapAsync"/>.
    /// </summary>
    /// <param name="mapping">The mapping.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the router answered or gave up.</returns>
    Task UnmapAsync(PortMappingResult mapping, CancellationToken cancellationToken);
}
