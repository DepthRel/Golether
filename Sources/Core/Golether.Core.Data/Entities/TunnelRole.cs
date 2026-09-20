namespace Golether.Core.Data.Entities;

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
