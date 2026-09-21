namespace Golether.Sync.Protocol;

/// <summary>
/// The sender leaves the session.
/// </summary>
/// <param name="Reason">An optional explanation; the host sends none, and the receiver words the end of the session
/// in its own language.</param>
public sealed record ByeMessage(string? Reason) : SessionMessage;
