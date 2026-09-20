namespace Golether.Components.GStreamer;

/// <summary>
/// A GStreamer installation already present on the computer.
/// </summary>
/// <param name="Root">The installation directory.</param>
/// <param name="Version">The version, or <see langword="null"/> when unknown.</param>
/// <param name="IsComplete">Whether it contains everything Golether needs.</param>
public sealed record ExistingGStreamer(string Root, Version? Version, bool IsComplete);
