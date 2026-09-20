namespace Golether.Sync.Protocol;

/// <summary>
/// A participant asks the host to change the playback state. Every participant may send it.
/// </summary>
/// <param name="Request">The request.</param>
public sealed record PlaybackRequestMessage(PlaybackRequest Request) : SessionMessage;
