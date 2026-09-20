namespace Golether.Media.Conference;

/// <summary>
/// A decoded video frame in BGRA format.
/// </summary>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="Stride">The number of bytes per row.</param>
/// <param name="Pixels">The pixel data; valid only during the event handler.</param>
public readonly record struct VideoFrame(int Width, int Height, int Stride, ReadOnlyMemory<byte> Pixels);
