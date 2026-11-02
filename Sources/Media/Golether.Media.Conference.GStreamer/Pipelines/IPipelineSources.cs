namespace Golether.Media.Conference.GStreamer.Pipelines;

/// <summary>
/// The capture and playback elements of one platform: the only part of the pipelines that differs between systems.
/// </summary>
public interface IPipelineSources
{
    /// <summary>
    /// Gets the element that plays the mixed sound of the other participants.
    /// </summary>
    string AudioSink { get; }

    /// <summary>
    /// Builds the element that captures the camera.
    /// </summary>
    /// <param name="camera">The camera or <see langword="null"/> for the default one.</param>
    /// <returns>The element with its properties, as a part of a pipeline description.</returns>
    string VideoSource(CaptureDevice? camera);

    /// <summary>
    /// Builds the element that captures the microphone.
    /// </summary>
    /// <param name="microphone">The microphone or <see langword="null"/> for the default one.</param>
    /// <returns>The element with its properties, as a part of a pipeline description.</returns>
    string AudioSource(CaptureDevice? microphone);
}
