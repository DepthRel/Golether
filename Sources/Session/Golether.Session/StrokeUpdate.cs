using Golether.Core.Data.Enums;
using Golether.Core.Identity;
using Golether.Sync.Protocol;

namespace Golether.Session;

/// <summary>
/// A piece of a stroke drawn over the video by a participant.
/// </summary>
/// <param name="Sender">The authenticated author.</param>
/// <param name="StrokeId">The stroke.</param>
/// <param name="Phase">Which part of the stroke this is.</param>
/// <param name="Points">The new points, as shares of the picture.</param>
/// <param name="IsLocal">Whether this device drew it.</param>
public sealed record StrokeUpdate(PeerId Sender, string StrokeId, StrokePhase Phase, IReadOnlyList<StrokePoint> Points, bool IsLocal);
