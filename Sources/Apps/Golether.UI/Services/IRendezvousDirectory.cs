using Golether.Core.Networking;

namespace Golether.UI.Services;

/// <summary>
/// Finds the rendezvous server of the project, so the user does not have to know its address.
/// </summary>
public interface IRendezvousDirectory
{
    /// <summary>
    /// Asks the page of the project for the address; when the page does not answer, the last address it gave is used.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The address, or <see langword="null"/> when none is known.</returns>
    Task<PeerEndpoint?> FindAsync(CancellationToken cancellationToken);
}
