namespace Golether.Web.Server.UI.Enums;

/// <summary>
/// How the server is doing, in one word for the colour of the status.
/// </summary>
public enum ServerCondition
{
    /// <summary>
    /// Not known yet.
    /// </summary>
    Unknown,

    /// <summary>
    /// Running and answering.
    /// </summary>
    Good,

    /// <summary>
    /// Changing its state, or running without being a service.
    /// </summary>
    Warning,

    /// <summary>
    /// Not running, or running without answering.
    /// </summary>
    Bad,
}
