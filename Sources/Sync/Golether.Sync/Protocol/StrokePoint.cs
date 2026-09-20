namespace Golether.Sync.Protocol;

/// <summary>
/// A point of a stroke as a share of the video picture, so every participant draws it in the right place whatever
/// the size of their window.
/// </summary>
/// <param name="X">The horizontal share, 0–1.</param>
/// <param name="Y">The vertical share, 0–1.</param>
public readonly record struct StrokePoint(float X, float Y);
