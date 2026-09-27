namespace Golether.Transports.Rendezvous;

/// <summary>
/// The device key a host holds its room with. The private key stays with the caller: the client only asks it to sign.
/// </summary>
/// <param name="Certificate">The device certificate (DER) with the ECDSA P-256 public key.</param>
/// <param name="Sign">Signs bytes with the device key (ECDSA, SHA-256).</param>
public sealed record RendezvousOwner(byte[] Certificate, Func<byte[], byte[]> Sign);
