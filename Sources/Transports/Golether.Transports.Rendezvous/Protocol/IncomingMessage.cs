namespace Golether.Transports.Rendezvous.Protocol;

/// <summary>
/// The server tells the host of a room that a participant waits.
/// </summary>
/// <param name="Punch">The identifier of the meeting.</param>
public sealed record IncomingMessage(string Punch) : RendezvousMessage;
