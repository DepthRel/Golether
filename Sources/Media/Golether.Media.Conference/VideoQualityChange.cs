using Golether.Core.Data.Enums;
using Golether.Core.Identity;

namespace Golether.Media.Conference;

/// <summary>
/// The camera quality sent to a participant changed.
/// </summary>
/// <param name="Peer">The participant.</param>
/// <param name="Quality">The new quality.</param>
public sealed record VideoQualityChange(PeerId Peer, VideoQuality Quality);
