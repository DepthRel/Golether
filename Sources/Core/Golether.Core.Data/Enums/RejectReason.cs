namespace Golether.Core.Data.Enums;

/// <summary>
/// The reason a participant was not admitted.
/// </summary>
public enum RejectReason
{
    /// <summary>
    /// The invitation token is unknown, used or expired.
    /// </summary>
    InvalidInvite = 0,

    /// <summary>
    /// The host rejected the participant.
    /// </summary>
    Declined = 1,

    /// <summary>
    /// The host did not answer in time.
    /// </summary>
    ApprovalTimedOut = 2,

    /// <summary>
    /// The session is full.
    /// </summary>
    SessionFull = 3,

    /// <summary>
    /// The protocol versions are incompatible.
    /// </summary>
    IncompatibleVersion = 4,

    /// <summary>
    /// The host removed the participant.
    /// </summary>
    Removed = 5,
}
