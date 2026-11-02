using System.Globalization;

namespace Golether.Media.Conference.GStreamer.Pipelines;

/// <summary>
/// The elements of Android: the camera and the sound of the system. The voice preset of the microphone and the voice
/// stream of the loudspeaker turn on the echo cancellation of the phone.
/// </summary>
internal sealed class AndroidPipelineSources : IPipelineSources
{
    /// <summary>
    /// The sizes the camera may give: left alone it chooses its smallest, and the picture turns into squares.
    /// </summary>
    private const string CAPTURE_SIZE = "video/x-raw,width=[640,1280],height=[360,720]";

    /// <inheritdoc />
    public string AudioSink => "openslessink stream-type=voice";

    /// <inheritdoc />
    public string VideoSource(CaptureDevice? camera)
    {
        if (camera is null)
        {
            return $"ahcsrc ! {CAPTURE_SIZE}";
        }

        var device = int.Parse(camera.Id, NumberStyles.None, CultureInfo.InvariantCulture);
        var source = $"ahcsrc device={device} ! {CAPTURE_SIZE}";

        return camera.Rotation switch
        {
            90 => $"{source} ! videoflip method=clockwise",
            180 => $"{source} ! videoflip method=rotate-180",
            270 => $"{source} ! videoflip method=counterclockwise",
            _ => source,
        };
    }

    /// <inheritdoc />
    public string AudioSource(CaptureDevice? microphone) => "openslessrc preset=voice-communication";
}
