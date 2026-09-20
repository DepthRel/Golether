using Golether.Core.Data.Enums;

namespace Golether.Core.Data.Entities;

/// <summary>
/// A tunnel offer or an established tunnel (table <c>Tunnels</c>).
/// </summary>
public sealed class TunnelEntity
{
    /// <summary>
    /// Gets or sets the record identifier.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the offer identifier.
    /// </summary>
    public string OfferId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the role of this device.
    /// </summary>
    public TunnelRole Role { get; set; }

    /// <summary>
    /// Gets or sets the status.
    /// </summary>
    public TunnelStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the local interface name.
    /// </summary>
    public string InterfaceName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tunnel address of the other side (participant address for hosts, host address for participants).
    /// </summary>
    public string TunnelAddress { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the device identifier of the other side, once known.
    /// </summary>
    public string? PeerId { get; set; }

    /// <summary>
    /// Gets or sets the name of the other side, once known.
    /// </summary>
    public string? PeerName { get; set; }

    /// <summary>
    /// Gets or sets protected data: offer secrets, the host peer section or the participant configuration.
    /// </summary>
    public byte[]? SecretData { get; set; }

    /// <summary>
    /// Gets or sets the creation time.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the time of the last change.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the expiry time of a pending offer.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}
