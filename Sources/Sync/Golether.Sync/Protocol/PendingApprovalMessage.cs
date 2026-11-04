namespace Golether.Sync.Protocol;

/// <summary>
/// The host received the hello and waits for the host user to approve the participant.
/// </summary>
public sealed record PendingApprovalMessage : SessionMessage
{
    /// <summary>
    /// Gets the random number of the host for the verification code, sent only after the participant committed to
    /// its own in the hello; 64 lowercase hexadecimal digits.
    /// </summary>
    public string? SasNonce { get; init; }
}
