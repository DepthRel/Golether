namespace Golether.Transports;

/// <summary>
/// Accepts authenticated streams from peers.
/// </summary>
public interface IPeerListener : IAsyncDisposable
{
    /// <summary>
    /// Gets the local port the listener is bound to.
    /// </summary>
    int Port { get; }

    /// <summary>
    /// Waits for the next authenticated stream.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stream; the caller owns it.</returns>
    ValueTask<PeerStream> AcceptAsync(CancellationToken cancellationToken);
}
