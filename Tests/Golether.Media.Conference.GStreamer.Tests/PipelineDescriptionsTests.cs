using Golether.Core.Data.Enums;
using Golether.Media.Conference.GStreamer.Pipelines;

namespace Golether.Media.Conference.GStreamer.Tests;

/// <summary>
/// Tests of the pipeline descriptions: the strings each platform gets are pinned, so moving them changes nothing.
/// </summary>
public sealed class PipelineDescriptionsTests
{
    /// <summary>
    /// The bitrate of the usual camera stream in the tests.
    /// </summary>
    private const int VIDEO_BITRATE = 600_000;

    /// <summary>
    /// The bitrate of the economy camera stream in the tests.
    /// </summary>
    private const int LOW_VIDEO_BITRATE = 150_000;

    /// <summary>
    /// A camera with a plain identifier.
    /// </summary>
    private static readonly CaptureDevice _camera = new("cam", "Camera", CaptureDeviceKind.Camera);

    /// <summary>
    /// A microphone with a plain identifier.
    /// </summary>
    private static readonly CaptureDevice _microphone = new("mic", "Microphone", CaptureDeviceKind.Microphone);

    /// <summary>
    /// Windows uses Media Foundation for the camera and WASAPI for the sound.
    /// </summary>
    [Fact]
    public void Windows_Sources()
    {
        var sources = new WindowsPipelineSources();

        Assert.Equal("mfvideosrc", sources.VideoSource(null));
        Assert.Equal("mfvideosrc device-path=\"cam\"", sources.VideoSource(_camera));
        Assert.Equal("wasapi2src low-latency=true", sources.AudioSource(null));
        Assert.Equal("wasapi2src low-latency=true device=\"mic\"", sources.AudioSource(_microphone));
        Assert.Equal("wasapi2sink low-latency=true", sources.AudioSink);
    }

    /// <summary>
    /// macOS uses AVFoundation for the camera and the default microphone whatever device was chosen.
    /// </summary>
    [Fact]
    public void Mac_Sources()
    {
        var sources = new MacPipelineSources();

        Assert.Equal("avfvideosrc", sources.VideoSource(null));
        Assert.Equal("avfvideosrc device-unique-id=\"cam\"", sources.VideoSource(_camera));
        Assert.Equal("osxaudiosrc", sources.AudioSource(null));
        Assert.Equal("osxaudiosrc", sources.AudioSource(_microphone));
        Assert.Equal("autoaudiosink", sources.AudioSink);
    }

    /// <summary>
    /// Linux uses the automatic elements by default, V4L2 and PulseAudio for a chosen device.
    /// </summary>
    [Fact]
    public void Linux_Sources()
    {
        var sources = new LinuxPipelineSources();

        Assert.Equal("autovideosrc", sources.VideoSource(null));
        Assert.Equal("v4l2src device=\"cam\"", sources.VideoSource(_camera));
        Assert.Equal("autoaudiosrc", sources.AudioSource(null));
        Assert.Equal("pulsesrc device=\"mic\"", sources.AudioSource(_microphone));
        Assert.Equal("autoaudiosink", sources.AudioSink);
    }

    /// <summary>
    /// Android uses the camera and the sound of the system: the voice preset turns on the echo cancellation of the
    /// phone, the voices play as media, and there is one camera and one microphone to choose from here.
    /// </summary>
    [Fact]
    public void Android_Sources()
    {
        var sources = new AndroidPipelineSources();

        Assert.Equal("ahcsrc ! video/x-raw,width=[640,1280],height=[360,720] ! videorate drop-only=true max-rate=15", sources.VideoSource(null));
        Assert.Equal("openslessrc preset=voice-communication", sources.AudioSource(null));
        Assert.Equal("openslessrc preset=voice-communication", sources.AudioSource(_microphone));
        Assert.Equal("openslessink stream-type=media", sources.AudioSink);
    }

    /// <summary>
    /// The camera of a phone is chosen by its number, and its picture is turned the way the screen is held.
    /// </summary>
    /// <param name="rotation">The turn of the picture.</param>
    /// <param name="flip">The element that turns it.</param>
    [Theory]
    [InlineData(0, "")]
    [InlineData(90, " ! videoflip method=clockwise")]
    [InlineData(180, " ! videoflip method=rotate-180")]
    [InlineData(270, " ! videoflip method=counterclockwise")]
    public void Android_TurnsThePictureOfTheCamera(int rotation, string flip)
    {
        var camera = new CaptureDevice("1", "Front", CaptureDeviceKind.Camera, rotation);

        var source = new AndroidPipelineSources().VideoSource(camera);

        Assert.Equal($"ahcsrc device=1 ! video/x-raw,width=[640,1280],height=[360,720] ! videorate drop-only=true max-rate=15{flip}", source);
    }

    /// <summary>
    /// Test sources replace the camera and the microphone but keep the loudspeaker of the platform.
    /// </summary>
    [Fact]
    public void Test_SourcesKeepTheSinkOfThePlatform()
    {
        var sources = new TestPipelineSources(new WindowsPipelineSources());

        Assert.Equal("videotestsrc is-live=true pattern=ball", sources.VideoSource(_camera));
        Assert.Equal("audiotestsrc is-live=true wave=sine freq=440 volume=0.2", sources.AudioSource(_microphone));
        Assert.Equal("wasapi2sink low-latency=true", sources.AudioSink);
    }

    /// <summary>
    /// The sources of the running platform are chosen, wrapped in test sources on request.
    /// </summary>
    [Fact]
    public void ForCurrentPlatform_ChoosesThePlatform()
    {
        var expected = OperatingSystem.IsWindows() ? typeof(WindowsPipelineSources)
            : OperatingSystem.IsMacOS() ? typeof(MacPipelineSources)
            : typeof(LinuxPipelineSources);

        Assert.IsType(expected, PipelineSources.ForCurrentPlatform(useTestSources: false));
        Assert.IsType<TestPipelineSources>(PipelineSources.ForCurrentPlatform(useTestSources: true));
    }

    /// <summary>
    /// Quotes and backslashes of a device identifier are escaped.
    /// </summary>
    [Fact]
    public void Quote_EscapesQuotesAndBackslashes()
    {
        Assert.Equal("\"a\\\"b\\\\c\"", PipelineText.Quote("a\"b\\c"));
        Assert.Equal("v4l2src device=\"a\\\"b\\\\c\"", new LinuxPipelineSources().VideoSource(new CaptureDevice("a\"b\\c", "Odd", CaptureDeviceKind.Camera)));
    }

    /// <summary>
    /// The camera pipeline: preview, the usual encoder and the gated economy encoder.
    /// </summary>
    [Fact]
    public void VideoCapture_IsPinned()
    {
        var description = PipelineDescriptions.VideoCapture(new WindowsPipelineSources(), _camera, VIDEO_BITRATE, LOW_VIDEO_BITRATE);

        Assert.Equal(
            "mfvideosrc device-path=\"cam\" ! videoconvert ! videoscale ! videorate ! " +
            "video/x-raw,width=640,height=360,framerate=15/1,pixel-aspect-ratio=1/1 ! tee name=split " +
            "split. ! queue leaky=downstream max-size-buffers=2 ! videoconvert ! video/x-raw,format=BGRA ! " +
            "appsink name=preview emit-signals=true max-buffers=1 drop=true sync=false " +
            "split. ! queue leaky=downstream max-size-buffers=5 ! videoconvert ! video/x-raw,format=I420 ! " +
            "vp8enc deadline=1 cpu-used=8 target-bitrate=600000 keyframe-max-dist=30 lag-in-frames=0 " +
            "error-resilient=partitions threads=2 ! appsink name=venc emit-signals=true sync=false max-buffers=10 drop=true " +
            "split. ! queue leaky=downstream max-size-buffers=5 ! valve name=lowgate drop=true drop-mode=forward-sticky-events ! videoscale ! videorate ! " +
            "video/x-raw,width=320,height=180,framerate=10/1,pixel-aspect-ratio=1/1 ! videoconvert ! video/x-raw,format=I420 ! " +
            "vp8enc deadline=1 cpu-used=16 target-bitrate=150000 keyframe-max-dist=10 lag-in-frames=0 " +
            "error-resilient=partitions threads=1 ! appsink name=venclow emit-signals=true sync=false async=false max-buffers=10 drop=true",
            description);
    }

    /// <summary>
    /// The microphone pipeline with echo cancellation.
    /// </summary>
    [Fact]
    public void AudioCapture_WithEchoCancellation_IsPinned()
    {
        var description = PipelineDescriptions.AudioCapture(new LinuxPipelineSources(), _microphone, echoCancellation: true);

        Assert.Equal(
            "pulsesrc device=\"mic\" ! audioconvert ! audioresample ! audio/x-raw,format=S16LE,rate=48000,channels=1 ! " +
            "webrtcdsp probe=golether-echo echo-cancel=true noise-suppression=true gain-control=true ! " +
            "audioconvert ! audio/x-raw,format=S16LE,rate=48000,channels=1 ! tee name=mic " +
            "mic. ! queue max-size-buffers=50 leaky=downstream ! opusenc bitrate=32000 frame-size=20 ! " +
            "appsink name=aenc emit-signals=true sync=false max-buffers=50 drop=true " +
            "mic. ! queue max-size-buffers=10 leaky=downstream ! appsink name=alevel emit-signals=true sync=false max-buffers=10 drop=true",
            description);
    }

    /// <summary>
    /// The microphone pipeline without echo cancellation leaves the canceller out.
    /// </summary>
    [Fact]
    public void AudioCapture_WithoutEchoCancellation_IsPinned()
    {
        var description = PipelineDescriptions.AudioCapture(new LinuxPipelineSources(), null, echoCancellation: false);

        Assert.Equal(
            "autoaudiosrc ! audioconvert ! audioresample ! audio/x-raw,format=S16LE,rate=48000,channels=1 ! " +
            "audioconvert ! audio/x-raw,format=S16LE,rate=48000,channels=1 ! tee name=mic " +
            "mic. ! queue max-size-buffers=50 leaky=downstream ! opusenc bitrate=32000 frame-size=20 ! " +
            "appsink name=aenc emit-signals=true sync=false max-buffers=50 drop=true " +
            "mic. ! queue max-size-buffers=10 leaky=downstream ! appsink name=alevel emit-signals=true sync=false max-buffers=10 drop=true",
            description);
    }

    /// <summary>
    /// The playback pipeline: one mixer input per remote peer, the echo probe before the loudspeaker.
    /// </summary>
    [Fact]
    public void Playback_IsPinned()
    {
        var description = PipelineDescriptions.Playback(new WindowsPipelineSources(), GStreamerConferenceMedia.MAX_REMOTE_PEERS);

        const string SLOT = " is-live=true do-timestamp=true format=time " +
                            "caps=\"audio/x-raw,format=S16LE,rate=48000,channels=1,layout=interleaved\" ! queue ! mix.";
        Assert.Equal(
            "audiomixer name=mix latency=60000000 ! audioconvert ! audioresample ! webrtcechoprobe name=golether-echo ! " +
            "audioconvert ! audioresample ! wasapi2sink low-latency=true" +
            " appsrc name=slot0" + SLOT +
            " appsrc name=slot1" + SLOT +
            " appsrc name=slot2" + SLOT +
            " appsrc name=slot3" + SLOT,
            description);
    }
}
