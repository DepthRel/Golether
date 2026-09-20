namespace Golether.Session;

/// <summary>
/// The connection state of the local side.
/// </summary>
public enum SessionState
{
    /// <summary>
    /// Connecting to the host.
    /// </summary>
    Connecting = 0,

    /// <summary>
    /// Waiting for the host to approve.
    /// </summary>
    AwaitingApproval = 1,

    /// <summary>
    /// Watching.
    /// </summary>
    Active = 2,

    /// <summary>
    /// The session ended.
    /// </summary>
    Ended = 3,
}
