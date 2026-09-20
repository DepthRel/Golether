namespace Golether.Transports.PortMapping;

/// <summary>
/// The state of the inbound rule of the application in the Windows firewall.
/// </summary>
public enum FirewallState
{
    /// <summary>
    /// The rule could not be read: the firewall service is off, or another program manages it.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Incoming connections to this copy of the application are allowed.
    /// </summary>
    Allowed = 1,

    /// <summary>
    /// There is no rule for this copy: participants from other networks will not get through.
    /// </summary>
    Missing = 2,
}
