using Golether.Core.Identity;
using Golether.Security.Verification;
using Golether.Tunnels.AmneziaWG.Configuration;

namespace Golether.Tunnels.AmneziaWG.Packages;

/// <summary>
/// The result of completing an offer on the host side.
/// </summary>
/// <param name="Peer">The peer section to add to the host interface.</param>
/// <param name="ParticipantPeerId">The verified participant device.</param>
/// <param name="ParticipantName">The participant name.</param>
/// <param name="VerificationCode">The code to compare with the participant by voice.</param>
public sealed record HostCompletion(AwgPeer Peer, PeerId ParticipantPeerId, string ParticipantName, VerificationCode VerificationCode);
