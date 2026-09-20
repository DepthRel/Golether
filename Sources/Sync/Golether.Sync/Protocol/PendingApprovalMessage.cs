namespace Golether.Sync.Protocol;

/// <summary>
/// The host received the hello and waits for the host user to approve the participant.
/// </summary>
public sealed record PendingApprovalMessage : SessionMessage;
