using System.Net;

namespace Golether.Transports.Relay;

/// <summary>
/// A relay that serves clients whose connections were opened by other means: the session hands it the streams the
/// participants opened over the route of the session.
/// </summary>
public interface IRelayStreamServer
{
    /// <summary>
    /// Serves one client until it leaves. The client speaks the same protocol as over a TCP connection and needs
    /// the same credentials.
    /// </summary>
    /// <param name="stream">The stream of the client; the caller closes it.</param>
    /// <param name="remote">The address of the client.</param>
    /// <param name="local">The address of this device the client reached; its relay is created there.</param>
    /// <param name="cancellationToken">Stops serving.</param>
    /// <returns>A task that completes when the client left or serving was stopped.</returns>
    Task ServeAsync(Stream stream, IPEndPoint remote, IPAddress local, CancellationToken cancellationToken);
}
