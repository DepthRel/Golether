namespace Golether.Transports.Rendezvous.Protocol;

/// <summary>
/// The answer of the server to <see cref="PingMessage"/>.
/// </summary>
public sealed record PongMessage : RendezvousMessage;
