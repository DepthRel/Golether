using System.Globalization;

namespace Golether.Media.Conference.GStreamer.Pipelines;

/// <summary>
/// The elements of Android: the camera and the sound of the system. The voice preset of the microphone turns on the
/// echo cancellation of the phone; the voices play as media, together with the film, so the keys of the phone move one
/// scale only and the sound stays on the headphones the film plays on.
/// </summary>
internal sealed class AndroidPipelineSources : IPipelineSources
{
    /// <summary>
    /// The sizes the camera may give: left alone it chooses its smallest, and the picture turns into squares.
    /// </summary>
    private const string CAPTURE_SIZE = "video/x-raw,width=[640,1280],height=[360,720]";

    /// <summary>
    /// Drops the frames the call does not send before anything is done with them: the camera gives up to thirty a
    /// second, the call sends fifteen, and each frame costs the processor a conversion and a turn.
    /// </summary>
    private const string FRAME_LIMIT = "videorate drop-only=true max-rate=15";

    /// <inheritdoc />
    public string AudioSink => "openslessink stream-type=media";

    /// <inheritdoc />
    public string VideoSource(CaptureDevice? camera)
    {
        if (camera is null)
        {
            return $"ahcsrc ! {CAPTURE_SIZE} ! {FRAME_LIMIT}";
        }

        var device = int.Parse(camera.Id, NumberStyles.None, CultureInfo.InvariantCulture);
        var source = $"ahcsrc device={device} ! {CAPTURE_SIZE} ! {FRAME_LIMIT}";

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
