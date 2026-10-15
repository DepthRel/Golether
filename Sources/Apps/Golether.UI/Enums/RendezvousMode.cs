namespace Golether.UI.Enums;

/// <summary>
/// How a hosted session lets participants from other networks in.
/// </summary>
public enum RendezvousMode
{
    /// <summary>
    /// The rendezvous server of the project, whose address the application finds by itself.
    /// </summary>
    Automatic,

    /// <summary>
    /// No rendezvous server: participants reach the addresses of the host only.
    /// </summary>
    Direct,

    /// <summary>
    /// The rendezvous server the user typed.
    /// </summary>
    Manual,
}
