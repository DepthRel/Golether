namespace Golether.Core.Playback;

/// <summary>
/// A part of the media timeline.
/// </summary>
/// <param name="Start">The start.</param>
/// <param name="End">The end.</param>
public readonly record struct MediaTimeRange(TimeSpan Start, TimeSpan End);
