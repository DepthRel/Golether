namespace Golether.Media.Conference.GStreamer;

/// <summary>
/// The servers a peer uses to find a path.
/// </summary>
/// <param name="StunServer">A STUN server or <see langword="null"/>.</param>
/// <param name="TurnServer">A TURN relay or <see langword="null"/>.</param>
/// <param name="RelayOnly">Whether only relayed paths are allowed.</param>
internal sealed record PeerNetwork(string? StunServer, string? TurnServer, bool RelayOnly);
