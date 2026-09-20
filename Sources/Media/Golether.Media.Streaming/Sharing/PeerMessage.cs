using Golether.Core.Identity;

namespace Golether.Media.Streaming.Sharing;

/// <summary>
/// A message from another participant.
/// </summary>
/// <param name="Peer">The authenticated sender.</param>
/// <param name="Data">The message; copy it to keep it.</param>
public readonly record struct PeerMessage(PeerId Peer, ReadOnlyMemory<byte> Data);
