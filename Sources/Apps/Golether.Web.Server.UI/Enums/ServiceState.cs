namespace Golether.Web.Server.UI.Enums;

/// <summary>
/// The state of the service of the server as the system reports it.
/// </summary>
public enum ServiceState
{
    /// <summary>
    /// The system could not be asked.
    /// </summary>
    Unknown,

    /// <summary>
    /// There is no such service on this computer.
    /// </summary>
    NotInstalled,

    /// <summary>
    /// The service is stopped.
    /// </summary>
    Stopped,

    /// <summary>
    /// The service is starting.
    /// </summary>
    Starting,

    /// <summary>
    /// The service is running.
    /// </summary>
    Running,

    /// <summary>
    /// The service is stopping.
    /// </summary>
    Stopping,
}
