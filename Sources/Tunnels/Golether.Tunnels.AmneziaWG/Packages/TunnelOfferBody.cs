namespace Golether.Tunnels.AmneziaWG.Packages;

/// <summary>
/// The body of a tunnel offer created by the host.
/// </summary>
public sealed record TunnelOfferBody
{
    /// <summary>
    /// Gets the random offer identifier (base64url).
    /// </summary>
    public required string OfferId { get; init; }

    /// <summary>
    /// Gets the host name.
    /// </summary>
    public required string HostName { get; init; }

    /// <summary>
    /// Gets the DER certificate of the host device (base64); it verifies the signature and yields the host identifier.
    /// </summary>
    public required string HostCertificate { get; init; }

    /// <summary>
    /// Gets the AmneziaWG public key of the host.
    /// </summary>
    public required string HostPublicKey { get; init; }

    /// <summary>
    /// Gets the candidate UDP endpoints of the host.
    /// </summary>
    public required IReadOnlyList<string> HostEndpoints { get; init; }

    /// <summary>
    /// Gets the host tunnel address.
    /// </summary>
    public required string HostAddress { get; init; }

    /// <summary>
    /// Gets the tunnel address assigned to the participant.
    /// </summary>
    public required string AssignedAddress { get; init; }

    /// <summary>
    /// Gets S1 of the shared obfuscation.
    /// </summary>
    public required int S1 { get; init; }

    /// <summary>
    /// Gets S2 of the shared obfuscation.
    /// </summary>
    public required int S2 { get; init; }

    /// <summary>
    /// Gets H1–H4 of the shared obfuscation.
    /// </summary>
    public required IReadOnlyList<uint> Headers { get; init; }

    /// <summary>
    /// Gets the ephemeral P-256 public key (SubjectPublicKeyInfo, base64) for the preshared key agreement.
    /// </summary>
    public required string EphemeralKey { get; init; }

    /// <summary>
    /// Gets the expiry time in Unix seconds.
    /// </summary>
    public required long ExpiresAt { get; init; }
}
