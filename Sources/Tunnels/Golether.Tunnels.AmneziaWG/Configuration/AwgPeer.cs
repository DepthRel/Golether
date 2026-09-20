using Golether.Core.Networking;

namespace Golether.Tunnels.AmneziaWG.Configuration;

/// <summary>
/// A <c>[Peer]</c> section of an AmneziaWG configuration.
/// </summary>
public sealed record AwgPeer
{
    /// <summary>
    /// Gets the comment written above the section (the participant name).
    /// </summary>
    public string? Comment { get; init; }

    /// <summary>
    /// Gets the base64 public key.
    /// </summary>
    public required string PublicKey { get; init; }

    /// <summary>
    /// Gets the base64 preshared key.
    /// </summary>
    public string? PresharedKey { get; init; }

    /// <summary>
    /// Gets the allowed addresses, for example <c>10.77.41.2/32</c>.
    /// </summary>
    public required IReadOnlyList<string> AllowedIps { get; init; }

    /// <summary>
    /// Gets the endpoint, or <see langword="null"/> when the peer connects to us.
    /// </summary>
    public PeerEndpoint? Endpoint { get; init; }

    /// <summary>
    /// Gets the keepalive interval in seconds, or <see langword="null"/>.
    /// </summary>
    public int? PersistentKeepalive { get; init; }

    /// <summary>
    /// Returns the section without exposing the preshared key in logs.
    /// </summary>
    /// <returns>The public key and addresses.</returns>
    public override string ToString() => $"AwgPeer {{ PublicKey = {PublicKey}, AllowedIps = {string.Join(", ", AllowedIps)} }}";
}
