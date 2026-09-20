namespace Golether.Sync.Protocol;

/// <summary>
/// The host answer to <see cref="ClockPingMessage"/>.
/// </summary>
/// <param name="ClientSendTime">The echoed participant time (t0).</param>
/// <param name="HostReceiveTime">The host time on receipt (t1).</param>
/// <param name="HostSendTime">The host time when answering (t2).</param>
public sealed record ClockPongMessage(long ClientSendTime, long HostReceiveTime, long HostSendTime) : SessionMessage;
