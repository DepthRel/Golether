namespace Golether.Media.Conference.GStreamer.Pipelines;

/// <summary>
/// The elements of Windows: Media Foundation for the camera, WASAPI for the sound.
/// </summary>
internal sealed class WindowsPipelineSources : IPipelineSources
{
    /// <inheritdoc />
    public string AudioSink => "wasapi2sink low-latency=true";

    /// <inheritdoc />
    public string VideoSource(CaptureDevice? camera)
        => "mfvideosrc" + (camera is null ? string.Empty : " device-path=" + PipelineText.Quote(camera.Id));

    /// <inheritdoc />
    public string AudioSource(CaptureDevice? microphone)
        => "wasapi2src low-latency=true" + (microphone is null ? string.Empty : " device=" + PipelineText.Quote(microphone.Id));
}
