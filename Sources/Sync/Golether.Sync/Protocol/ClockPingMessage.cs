namespace Golether.Sync.Protocol;

/// <summary>
/// A clock synchronization probe sent by a participant.
/// </summary>
/// <param name="ClientSendTime">The participant monotonic time when sending (t0).</param>
public sealed record ClockPingMessage(long ClientSendTime) : SessionMessage;
