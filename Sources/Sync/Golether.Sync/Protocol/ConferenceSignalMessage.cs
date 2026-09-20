using Golether.Core.Identity;

namespace Golether.Sync.Protocol;

/// <summary>
/// WebRTC signaling for cameras and voices (SDP or ICE candidate). A participant sends it with the target in
/// <paramref name="Peer"/>; the host forwards it with the authenticated source in <paramref name="Peer"/>.
/// </summary>
/// <param name="Peer">The target (to the host) or the source (from the host).</param>
/// <param name="Kind">The kind: <c>offer</c>, <c>answer</c> or <c>candidate</c>.</param>
/// <param name="Payload">The payload.</param>
public sealed record ConferenceSignalMessage(PeerId Peer, string Kind, string Payload) : SessionMessage;
