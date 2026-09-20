using Golether.Core.Session;

namespace Golether.Sync.Protocol;

/// <summary>
/// The participant list and statuses broadcast by the host.
/// </summary>
/// <param name="Participants">The participants, host included.</param>
/// <param name="Statuses">The latest statuses.</param>
public sealed record ParticipantsMessage(IReadOnlyList<ParticipantInfo> Participants, IReadOnlyList<ParticipantStatus> Statuses) : SessionMessage;
