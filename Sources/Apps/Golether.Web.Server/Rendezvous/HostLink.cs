using System.Net;
using Golether.Core.Identity;
using Golether.Transports.Rendezvous.Protocol;

namespace Golether.Web.Server.Rendezvous;

/// <summary>
/// The registration connection of a host that holds a room.
/// </summary>
internal sealed class HostLink : IDisposable
{
    /// <summary>
    /// The channel of the registration connection.
    /// </summary>
    private readonly RendezvousChannel _channel;

    /// <summary>
    /// Cancelled when the room is taken over or the connection ends.
    /// </summary>
    private readonly CancellationTokenSource _closed = new();

    /// <summary>
    /// The meetings waiting for this host.
    /// </summary>
    private int _pending;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostLink"/> class.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="owner">The device key the room belongs to.</param>
    /// <param name="observed">The address of the host as the server sees it.</param>
    /// <param name="channel">The channel of the registration connection.</param>
    public HostLink(string room, PeerId owner, IPEndPoint observed, RendezvousChannel channel)
    {
        Room = room;
        Owner = owner;
        Observed = observed;
        _channel = channel;

        // The token is taken once: participants still read it after the host has left and the source is disposed,
        // and asking a disposed source for its token throws.
        Closed = _closed.Token;
    }

    /// <summary>
    /// Gets the room.
    /// </summary>
    public string Room { get; }

    /// <summary>
    /// Gets the device key the room belongs to.
    /// </summary>
    public PeerId Owner { get; }

    /// <summary>
    /// Gets the address of the host as the server sees it.
    /// </summary>
    public IPEndPoint Observed { get; }

    /// <summary>
    /// Gets a token cancelled when the room is taken over or the connection ends.
    /// </summary>
    public CancellationToken Closed { get; }

    /// <summary>
    /// Sends a message to the host.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the message is sent.</returns>
    public ValueTask SendAsync(RendezvousMessage message, CancellationToken cancellationToken) => _channel.SendAsync(message, cancellationToken);

    /// <summary>
    /// Counts a new waiting meeting.
    /// </summary>
    /// <param name="limit">The largest number of waiting meetings.</param>
    /// <returns><see langword="false"/> when the host has too many already.</returns>
    public bool TryAddPending(int limit)
    {
        if (Interlocked.Increment(ref _pending) <= limit)
        {
            return true;
        }

        Interlocked.Decrement(ref _pending);
        return false;
    }

    /// <summary>
    /// Counts a finished meeting.
    /// </summary>
    public void RemovePending() => Interlocked.Decrement(ref _pending);

    /// <summary>
    /// Ends the registration: the connection handler of the host notices and closes the connection.
    /// </summary>
    public void Close()
    {
        try
        {
            _closed.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <inheritdoc />
    public void Dispose() => _closed.Dispose();
}
