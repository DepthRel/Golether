namespace Golether.Transports.Rendezvous.Protocol;

/// <summary>
/// A host takes a room and keeps the connection open to hear about participants.
/// </summary>
/// <param name="Room">The room.</param>
/// <param name="Certificate">The device certificate of the host (DER, base64).</param>
/// <param name="Signature">
/// The signature of <see cref="RendezvousSignature.BuildPayload"/> over the nonce and the room (base64).
/// </param>
public sealed record RegisterMessage(string Room, string Certificate, string Signature) : RendezvousMessage;
