using Golether.Core.Session;

namespace Golether.Sync.Protocol;

/// <summary>
/// The periodic status of a participant. The host replaces the peer identifier with the authenticated one.
/// </summary>
/// <param name="Status">The status.</param>
public sealed record StatusReportMessage(ParticipantStatus Status) : SessionMessage;
