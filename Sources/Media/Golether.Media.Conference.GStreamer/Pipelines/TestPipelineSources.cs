namespace Golether.Media.Conference.GStreamer.Pipelines;

/// <summary>
/// Test patterns instead of the camera and the microphone; the sound is played by the platform.
/// </summary>
/// <param name="inner">The elements of the platform, which give the loudspeaker.</param>
internal sealed class TestPipelineSources(IPipelineSources inner) : IPipelineSources
{
    /// <inheritdoc />
    public string AudioSink => inner.AudioSink;

    /// <inheritdoc />
    public string VideoSource(CaptureDevice? camera) => "videotestsrc is-live=true pattern=ball";

    /// <inheritdoc />
    public string AudioSource(CaptureDevice? microphone) => "audiotestsrc is-live=true wave=sine freq=440 volume=0.2";
}
