using Golether.Security.Verification;
using Golether.Tunnels.AmneziaWG.Configuration;

namespace Golether.UI.Services;

/// <summary>
/// The participant side of a tunnel negotiation.
/// </summary>
/// <param name="AnswerText">The answer package for the host.</param>
/// <param name="HostName">The host name.</param>
/// <param name="HostAddress">The host tunnel address (use it in the invitation instead of the public address).</param>
/// <param name="VerificationCode">The code to compare by voice.</param>
/// <param name="InterfaceName">The participant interface name.</param>
/// <param name="Configuration">The participant configuration.</param>
public sealed record ParticipantTunnelResult(
    string AnswerText,
    string HostName,
    string HostAddress,
    VerificationCode VerificationCode,
    string InterfaceName,
    AwgConfiguration Configuration);
