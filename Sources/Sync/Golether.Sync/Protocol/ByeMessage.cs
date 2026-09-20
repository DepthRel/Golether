namespace Golether.Sync.Protocol;

/// <summary>
/// The sender leaves the session.
/// </summary>
/// <param name="Reason">An optional explanation.</param>
public sealed record ByeMessage(string? Reason) : SessionMessage;
