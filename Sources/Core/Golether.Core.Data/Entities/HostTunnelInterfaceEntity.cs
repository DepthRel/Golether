namespace Golether.Core.Data.Entities;

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
