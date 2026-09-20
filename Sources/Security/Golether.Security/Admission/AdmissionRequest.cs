using Golether.Core.Identity;
using Golether.Security.Verification;

namespace Golether.Security.Admission;

/// <summary>
/// A request of a peer to join the session.
/// </summary>
/// <param name="PeerId">The authenticated device identifier.</param>
/// <param name="DisplayName">The name the peer introduced itself with.</param>
/// <param name="VerificationCode">The code both sides compare by voice.</param>
/// <param name="IsKnownContact">Whether the device is a trusted contact.</param>
public sealed record AdmissionRequest(PeerId PeerId, string DisplayName, VerificationCode VerificationCode, bool IsKnownContact);
