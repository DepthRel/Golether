using Golether.Core.Playback;

namespace Golether.Sync.Protocol;

/// <summary>
/// The authoritative playback state broadcast by the host.
/// </summary>
/// <param name="State">The state.</param>
public sealed record PlaybackStateMessage(PlaybackState State) : SessionMessage;
