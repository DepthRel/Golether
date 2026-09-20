using Golether.Core.Identity;

namespace Golether.Media.Conference;

/// <summary>
/// A change of the speaking state of a participant.
/// </summary>
/// <param name="PeerId">The participant, or <see langword="default"/> for this device.</param>
/// <param name="IsSpeaking">Whether the participant speaks now.</param>
public readonly record struct SpeakingChange(PeerId PeerId, bool IsSpeaking);
