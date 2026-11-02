namespace Golether.Media.Conference.GStreamer.Pipelines;

/// <summary>
/// The elements of macOS: AVFoundation for the camera, Core Audio for the sound.
/// </summary>
internal sealed class MacPipelineSources : IPipelineSources
{
    /// <inheritdoc />
    public string AudioSink => "autoaudiosink";

    /// <inheritdoc />
    public string VideoSource(CaptureDevice? camera)
        => "avfvideosrc" + (camera is null ? string.Empty : " device-unique-id=" + PipelineText.Quote(camera.Id));

    /// <summary>
    /// Builds the element that captures the microphone: the default microphone of the system, whatever was chosen.
    /// </summary>
    /// <param name="microphone">The chosen microphone; not used.</param>
    /// <returns>The element.</returns>
    public string AudioSource(CaptureDevice? microphone) => "osxaudiosrc";
}
