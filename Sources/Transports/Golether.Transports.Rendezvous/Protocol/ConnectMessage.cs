namespace Golether.Transports.Rendezvous.Protocol;

/// <summary>
/// A participant asks to meet the host of a room. The connection comes from the port the participant punches from.
/// </summary>
/// <param name="Room">The room.</param>
/// <param name="Locals">The local addresses of that port, useful when both sides are behind the same router.</param>
public sealed record ConnectMessage(string Room, string[] Locals) : RendezvousMessage;
