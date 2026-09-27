namespace Golether.Transports.Rendezvous.Protocol;

/// <summary>
/// The host answers a meeting on a new connection, from the port it punches from.
/// </summary>
/// <param name="Punch">The identifier of the meeting.</param>
/// <param name="Locals">The local addresses of that port.</param>
public sealed record AcceptMessage(string Punch, string[] Locals) : RendezvousMessage;
