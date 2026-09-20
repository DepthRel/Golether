using Golether.Core.Data.Enums;
using Golether.Core.Identity;

namespace Golether.Sync.Protocol;

/// <summary>
/// A piece of a stroke drawn over the video. Participants send it without <paramref name="Sender"/>; the host sets
/// the authenticated sender and relays it to everybody.
/// </summary>
/// <param name="StrokeId">The identifier of the stroke, chosen by the sender.</param>
/// <param name="Phase">Which part of the stroke this is.</param>
/// <param name="Points">The new points, in order.</param>
/// <param name="Sender">The sender, set by the host only.</param>
public sealed record DrawMessage(
    string StrokeId,
    StrokePhase Phase,
    IReadOnlyList<StrokePoint> Points,
    PeerId? Sender) : SessionMessage
{
    /// <summary>
    /// The largest number of points in one message.
    /// </summary>
    public const int MaxPoints = 128;

    /// <summary>
    /// The largest length of a stroke identifier.
    /// </summary>
    public const int MaxIdLength = 32;

    /// <summary>
    /// Checks a received message and brings its points into the picture.
    /// </summary>
    /// <returns>The clean message, or <see langword="null"/> when it must be dropped.</returns>
    public DrawMessage? Sanitize()
    {
        if (string.IsNullOrEmpty(StrokeId) || StrokeId.Length > MaxIdLength || !StrokeId.All(char.IsAsciiHexDigit)
            || !Enum.IsDefined(Phase) || Points is null || Points.Count > MaxPoints)
        {
            return null;
        }

        if (Points.Count == 0)
        {
            return Phase == StrokePhase.End ? this with { Points = [] } : null;
        }

        var points = new List<StrokePoint>(Points.Count);
        foreach (var point in Points)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
            {
                continue;
            }

            points.Add(new StrokePoint(Math.Clamp(point.X, 0, 1), Math.Clamp(point.Y, 0, 1)));
        }

        return points.Count == 0 && Phase != StrokePhase.End ? null : this with { Points = points };
    }

    /// <summary>
    /// Creates a message of this device.
    /// </summary>
    /// <param name="strokeId">The stroke.</param>
    /// <param name="phase">The part of the stroke.</param>
    /// <param name="points">The points.</param>
    /// <returns>The message, or <see langword="null"/> when there is nothing to send.</returns>
    public static DrawMessage? Create(string strokeId, StrokePhase phase, IReadOnlyList<StrokePoint> points)
        => new DrawMessage(strokeId, phase, points ?? [], null).Sanitize();

    /// <summary>
    /// Creates an identifier for a new stroke.
    /// </summary>
    /// <returns>The identifier.</returns>
    public static string CreateStrokeId() => Convert.ToHexString(Guid.NewGuid().ToByteArray())[..24];
}
