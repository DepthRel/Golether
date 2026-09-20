namespace Golether.Core.Data.Enums;

/// <summary>
/// The result of checking an invitation token.
/// </summary>
public enum InviteCheckResult
{
    /// <summary>
    /// The token is valid and has been consumed.
    /// </summary>
    Accepted = 0,

    /// <summary>
    /// The token is unknown or was already used.
    /// </summary>
    Unknown = 1,

    /// <summary>
    /// The token has expired.
    /// </summary>
    Expired = 2,
}
