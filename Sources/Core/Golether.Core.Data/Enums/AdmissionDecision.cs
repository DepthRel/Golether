namespace Golether.Core.Data.Enums;

/// <summary>
/// The outcome of an admission request.
/// </summary>
public enum AdmissionDecision
{
    /// <summary>
    /// The peer may join.
    /// </summary>
    Approved = 0,

    /// <summary>
    /// The host rejected the peer.
    /// </summary>
    Rejected = 1,

    /// <summary>
    /// The host did not answer in time.
    /// </summary>
    TimedOut = 2,
}
