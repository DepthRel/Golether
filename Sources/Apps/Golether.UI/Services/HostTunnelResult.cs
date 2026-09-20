using Golether.Security.Verification;
using Golether.Tunnels.AmneziaWG.Configuration;

namespace Golether.UI.Services;

/// <summary>
/// The host side of a completed tunnel negotiation.
/// </summary>
/// <param name="ParticipantName">The participant name.</param>
/// <param name="VerificationCode">The code to compare by voice.</param>
/// <param name="InterfaceName">The host interface name.</param>
/// <param name="Configuration">The host configuration with all participants.</param>
public sealed record HostTunnelResult(string ParticipantName, VerificationCode VerificationCode, string InterfaceName, AwgConfiguration Configuration);
