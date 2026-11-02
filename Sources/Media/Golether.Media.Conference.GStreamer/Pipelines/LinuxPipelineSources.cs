namespace Golether.Media.Conference.GStreamer.Pipelines;

/// <summary>
/// The elements of Linux: the automatic ones by default, V4L2 and PulseAudio for a chosen device.
/// </summary>
internal sealed class LinuxPipelineSources : IPipelineSources
{
    /// <inheritdoc />
    public string AudioSink => "autoaudiosink";

    /// <inheritdoc />
    public string VideoSource(CaptureDevice? camera)
        => camera is null ? "autovideosrc" : "v4l2src device=" + PipelineText.Quote(camera.Id);

    /// <inheritdoc />
    public string AudioSource(CaptureDevice? microphone)
        => microphone is null ? "autoaudiosrc" : "pulsesrc device=" + PipelineText.Quote(microphone.Id);
}
