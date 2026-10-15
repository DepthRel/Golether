namespace Golether.Sync.Protocol;

/// <summary>
/// The sender leaves the session.
/// </summary>
/// <param name="Reason">Kept for compatibility with earlier versions and always sent empty: the receiver ignores it and
/// words the end of the session in its own language.</param>
public sealed record ByeMessage(string? Reason) : SessionMessage;
