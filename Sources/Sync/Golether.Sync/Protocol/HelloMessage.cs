namespace Golether.Sync.Protocol;

/// <summary>
/// The first message of a participant.
/// </summary>
/// <param name="Version">The protocol version of the participant.</param>
/// <param name="DisplayName">The participant name.</param>
/// <param name="InviteToken">The invitation token; <see langword="null"/> when rejoining as a known participant.</param>
public sealed record HelloMessage(int Version, string DisplayName, string? InviteToken) : SessionMessage;
