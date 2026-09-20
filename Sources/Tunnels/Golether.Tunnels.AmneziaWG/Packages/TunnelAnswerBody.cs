namespace Golether.Tunnels.AmneziaWG.Packages;

/// <summary>
/// The body of a tunnel answer created by the participant.
/// </summary>
public sealed record TunnelAnswerBody
{
    /// <summary>
    /// Gets the identifier of the answered offer.
    /// </summary>
    public required string OfferId { get; init; }

    /// <summary>
    /// Gets the participant name.
    /// </summary>
    public required string ParticipantName { get; init; }

    /// <summary>
    /// Gets the DER certificate of the participant device (base64).
    /// </summary>
    public required string ParticipantCertificate { get; init; }

    /// <summary>
    /// Gets the AmneziaWG public key of the participant.
    /// </summary>
    public required string ParticipantPublicKey { get; init; }

    /// <summary>
    /// Gets the candidate UDP endpoints of the participant (may be empty).
    /// </summary>
    public required IReadOnlyList<string> ParticipantEndpoints { get; init; }

    /// <summary>
    /// Gets the ephemeral P-256 public key (SubjectPublicKeyInfo, base64).
    /// </summary>
    public required string EphemeralKey { get; init; }
}
