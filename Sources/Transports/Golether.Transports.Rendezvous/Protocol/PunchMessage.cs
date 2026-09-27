namespace Golether.Transports.Rendezvous.Protocol;

/// <summary>
/// The server tells both sides where the other one is; both start connecting at once.
/// </summary>
/// <param name="Peer">The public address of the other side as the server saw it.</param>
/// <param name="PeerLocals">
/// The local addresses of the other side; sent only when both sides share the public address (one router), empty
/// otherwise.
/// </param>
public sealed record PunchMessage(string Peer, string[] PeerLocals) : RendezvousMessage;
