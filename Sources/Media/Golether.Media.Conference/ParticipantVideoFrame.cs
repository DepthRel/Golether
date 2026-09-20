using Golether.Core.Identity;

namespace Golether.Media.Conference;

/// <summary>
/// A frame of a participant camera.
/// </summary>
/// <param name="PeerId">The participant, or <see langword="default"/> for the local preview.</param>
/// <param name="Frame">The frame.</param>
public readonly record struct ParticipantVideoFrame(PeerId PeerId, VideoFrame Frame);
