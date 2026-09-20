namespace Golether.UI.Controls;

/// <summary>
/// A part of the timeline as fractions of the duration.
/// </summary>
/// <param name="Start">The start, 0–1.</param>
/// <param name="End">The end, 0–1.</param>
public readonly record struct FractionRange(double Start, double End);
