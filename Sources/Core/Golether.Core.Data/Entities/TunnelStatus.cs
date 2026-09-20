namespace Golether.Core.Data.Entities;

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
