using Golether.Core.Identity;

namespace Golether.UI.ViewModels;

/// <summary>
/// The colour of a participant: the avatar on the tile, the marker on the timeline and the strokes they draw over
/// the video all use the same one, so it is clear who did what.
/// </summary>
public static class ParticipantColors
{
    /// <summary>
    /// The colours, picked by the device identifier.
    /// </summary>
    private static readonly string[] Palette = ["#8FB8F0", "#E59BC4", "#B6D77A", "#F0A860", "#9DD6CF", "#C9A6F2"];

    /// <summary>
    /// Returns the colour of a participant.
    /// </summary>
    /// <param name="peerId">The device identifier.</param>
    /// <returns>The colour as <c>#RRGGBB</c>.</returns>
    public static string For(PeerId peerId)
        => peerId.Value is { Length: >= 2 } value
            ? Palette[Convert.ToInt32(value[..2], 16) % Palette.Length]
            : Palette[0];
}
