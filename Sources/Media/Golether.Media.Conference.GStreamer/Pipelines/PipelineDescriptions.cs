namespace Golether.Media.Conference.GStreamer.Pipelines;

/// <summary>
/// Builds the descriptions of the camera, microphone and playback pipelines from the elements of a platform.
/// </summary>
internal static class PipelineDescriptions
{
    /// <summary>
    /// Builds the video capture description: a preview, the usual encoder and the economy encoder behind a valve.
    /// </summary>
    /// <param name="sources">The elements of the platform.</param>
    /// <param name="camera">The camera or <see langword="null"/> for the default.</param>
    /// <param name="videoBitrate">The bitrate of the usual stream, bits per second.</param>
    /// <param name="lowVideoBitrate">The bitrate of the economy stream, bits per second.</param>
    /// <returns>The description.</returns>
    public static string VideoCapture(IPipelineSources sources, CaptureDevice? camera, int videoBitrate, int lowVideoBitrate)
    {
        var source = sources.VideoSource(camera);

        return $"{source} ! videoconvert ! videoscale ! videorate ! " +
               "video/x-raw,width=640,height=360,framerate=15/1,pixel-aspect-ratio=1/1 ! tee name=split " +
               "split. ! queue leaky=downstream max-size-buffers=2 ! videoconvert ! video/x-raw,format=BGRA ! " +
               "appsink name=preview emit-signals=true max-buffers=1 drop=true sync=false " +
               "split. ! queue leaky=downstream max-size-buffers=5 ! videoconvert ! video/x-raw,format=I420 ! " +
               $"vp8enc deadline=1 cpu-used=8 target-bitrate={videoBitrate} keyframe-max-dist=30 lag-in-frames=0 " +
               "error-resilient=partitions threads=2 ! appsink name=venc emit-signals=true sync=false max-buffers=10 drop=true " +
               "split. ! queue leaky=downstream max-size-buffers=5 ! valve name=lowgate drop=true drop-mode=forward-sticky-events ! videoscale ! videorate ! " +
               "video/x-raw,width=320,height=180,framerate=10/1,pixel-aspect-ratio=1/1 ! videoconvert ! video/x-raw,format=I420 ! " +
               $"vp8enc deadline=1 cpu-used=16 target-bitrate={lowVideoBitrate} keyframe-max-dist=10 lag-in-frames=0 " +
               "error-resilient=partitions threads=1 ! appsink name=venclow emit-signals=true sync=false async=false max-buffers=10 drop=true";
    }

    /// <summary>
    /// Builds the audio capture description.
    /// </summary>
    /// <param name="sources">The elements of the platform.</param>
    /// <param name="microphone">The microphone or <see langword="null"/> for the default.</param>
    /// <param name="echoCancellation">Whether to use <c>webrtcdsp</c>.</param>
    /// <returns>The description.</returns>
    public static string AudioCapture(IPipelineSources sources, CaptureDevice? microphone, bool echoCancellation)
    {
        var source = sources.AudioSource(microphone);
        var dsp = echoCancellation ? "webrtcdsp probe=golether-echo echo-cancel=true noise-suppression=true gain-control=true ! " : string.Empty;

        return $"{source} ! audioconvert ! audioresample ! audio/x-raw,format=S16LE,rate=48000,channels=1 ! {dsp}" +
               "audioconvert ! audio/x-raw,format=S16LE,rate=48000,channels=1 ! tee name=mic " +
               "mic. ! queue max-size-buffers=50 leaky=downstream ! opusenc bitrate=32000 frame-size=20 ! " +
               "appsink name=aenc emit-signals=true sync=false max-buffers=50 drop=true " +
               "mic. ! queue max-size-buffers=10 leaky=downstream ! appsink name=alevel emit-signals=true sync=false max-buffers=10 drop=true";
    }

    /// <summary>
    /// Builds the playback description with one mixer input per remote peer.
    /// </summary>
    /// <param name="sources">The elements of the platform.</param>
    /// <param name="remotePeers">The number of mixer inputs.</param>
    /// <returns>The description.</returns>
    public static string Playback(IPipelineSources sources, int remotePeers)
    {
        var slots = string.Concat(Enumerable.Range(0, remotePeers).Select(i =>
            $" appsrc name=slot{i} is-live=true do-timestamp=true format=time " +
            "caps=\"audio/x-raw,format=S16LE,rate=48000,channels=1,layout=interleaved\" ! queue ! mix."));

        return "audiomixer name=mix latency=60000000 ! audioconvert ! audioresample ! webrtcechoprobe name=golether-echo ! " +
               $"audioconvert ! audioresample ! {sources.AudioSink}{slots}";
    }
}
