namespace Golether.Transports.Rendezvous.Protocol;

/// <summary>
/// The first message of the server on every connection.
/// </summary>
/// <param name="Version">The protocol version of the server.</param>
/// <param name="Nonce">A random value the host signs to prove it owns its device key.</param>
public sealed record ServerHelloMessage(int Version, string Nonce) : RendezvousMessage;
