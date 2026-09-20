namespace Golether.Media.Conference.GStreamer.Native;

/// <summary>
/// Receives mapped sample data.
/// </summary>
/// <param name="data">The data, valid only during the call.</param>
/// <param name="caps">The caps of the sample (not owned).</param>
internal delegate void SampleConsumer(ReadOnlySpan<byte> data, nint caps);
