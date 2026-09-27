namespace Golether.Transports.Rendezvous.Protocol;

/// <summary>
/// The server confirms the room.
/// </summary>
/// <param name="Observed">The address of the host as the server sees it, for diagnostics.</param>
public sealed record RegisteredMessage(string Observed) : RendezvousMessage;
