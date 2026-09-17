namespace Golether.Core.Data.Entities;

/// <summary>
/// A known device of another participant (table <c>Contacts</c>).
/// </summary>
public sealed class ContactEntity
{
    /// <summary>
    /// Gets or sets the device identifier (64 hexadecimal characters).
    /// </summary>
    public string PeerId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the last known name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the user marked the device as trusted.
    /// </summary>
    public bool IsTrusted { get; set; }

    /// <summary>
    /// Gets or sets the last address the device was reached at.
    /// </summary>
    public string? LastEndpoint { get; set; }

    /// <summary>
    /// Gets or sets user notes.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the time the device was first seen.
    /// </summary>
    public DateTimeOffset FirstSeenAt { get; set; }

    /// <summary>
    /// Gets or sets the time the device was last seen.
    /// </summary>
    public DateTimeOffset LastSeenAt { get; set; }
}

/// <summary>
/// The host AmneziaWG interface of this device (table <c>HostTunnelInterfaces</c>).
/// </summary>
public sealed class HostTunnelInterfaceEntity
{
    /// <summary>
    /// Gets or sets the interface name.
    /// </summary>
    public string InterfaceName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the protected interface data (keys and parameters).
    /// </summary>
    public byte[] SecretData { get; set; } = [];

    /// <summary>
    /// Gets or sets the creation time.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// The role of this device in a tunnel.
/// </summary>
public enum TunnelRole
{
    /// <summary>
    /// This device hosts the tunnel.
    /// </summary>
    Host = 0,

    /// <summary>
    /// This device joined the tunnel of another host.
    /// </summary>
    Participant = 1,
}

/// <summary>
/// The state of a tunnel record.
/// </summary>
public enum TunnelStatus
{
    /// <summary>
    /// The offer was sent; the answer is awaited.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// The tunnel is configured.
    /// </summary>
    Ready = 1,

    /// <summary>
    /// The tunnel was revoked.
    /// </summary>
    Revoked = 2,
}

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

/// <summary>
/// An application setting (table <c>Settings</c>).
/// </summary>
public sealed class SettingEntity
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the value.
    /// </summary>
    public string Value { get; set; } = string.Empty;
}
