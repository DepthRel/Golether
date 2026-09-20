using Golether.Core.Identity;
using Golether.Security.Verification;
using Golether.Tunnels.AmneziaWG.Configuration;

namespace Golether.Tunnels.AmneziaWG.Packages;

/// <summary>
/// The result of accepting an offer on the participant side.
/// </summary>
/// <param name="AnswerText">The text to send back to the host.</param>
/// <param name="Configuration">The participant tunnel configuration.</param>
/// <param name="HostPeerId">The verified host device.</param>
/// <param name="HostName">The host name.</param>
/// <param name="HostAddress">The host tunnel address to connect to inside the tunnel.</param>
/// <param name="VerificationCode">The code to compare with the host by voice.</param>
public sealed record ParticipantAcceptance(
    string AnswerText,
    AwgConfiguration Configuration,
    PeerId HostPeerId,
    string HostName,
    string HostAddress,
    VerificationCode VerificationCode);
