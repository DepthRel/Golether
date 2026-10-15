using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using Golether.Components.Catalog;
using Golether.Components.GStreamer;
using Golether.Media.Conference.GStreamer.Native;

namespace Golether.Media.Conference.GStreamer.Tests;

/// <summary>
/// The echo canceller across the playback and the microphone pipelines, without audio devices: what is "played"
/// comes back as what is "recorded", as it does in a room with loudspeakers.
/// </summary>
public sealed class EchoCancellationTests
{
    /// <summary>
    /// The format of the sound on both sides.
    /// </summary>
    private const string FORMAT = "audio/x-raw,format=S16LE,rate=48000,channels=1,layout=interleaved";

    /// <summary>
    /// The length of one block of sound in nanoseconds (10 ms).
    /// </summary>
    private const ulong BLOCK_NANOSECONDS = 10_000_000;

    /// <summary>
    /// How many blocks later the played sound reaches the microphone (60 ms).
    /// </summary>
    private const int ROOM_DELAY_BLOCKS = 6;

    /// <summary>
    /// A microphone pipeline started later than the playback, as on every switching on of the microphone, removes
    /// the echo of what is played when both pipelines count time from the same moment. With each counting from its
    /// own start the canceller gets silence for a reference and the echo passes at full loudness (about −17 dBFS here).
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Echo_IsRemoved_WhenThePipelinesShareATimeline()
    {
        var bundle = FindBundle();
        Assert.SkipWhen(bundle is null && OperatingSystem.IsWindows(), "The GStreamer bundle is not prepared (run golether-components fetch).");
        var registry = Path.Combine(Path.GetTempPath(), "golether-tests", "gst-registry.bin");
        Assert.SkipWhen(!GstRuntime.TryInitialize(bundle, registry, out var reason), reason ?? string.Empty);
        var token = TestContext.Current.CancellationToken;
        var origin = GstPipeline.SystemTime();

        using var playback = new GstPipeline(
            "playback",
            $"appsrc name=src is-live=true format=time caps=\"{FORMAT}\" ! queue ! webrtcechoprobe name=golether-test-echo ! fakesink sync=true",
            NullLogger.Instance);
        playback.UseTimeline(origin);
        playback.Play();

        GstPipeline? microphone = null;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var room = Task.Run(() => PlayAndEchoAsync(playback, () => Volatile.Read(ref microphone), origin, stop.Token), token);

        await Task.Delay(1500, token);
        using var recording = new GstPipeline(
            "microphone",
            $"appsrc name=src is-live=true format=time caps=\"{FORMAT}\" ! queue ! " +
            "webrtcdsp probe=golether-test-echo echo-cancel=true noise-suppression=false gain-control=false ! " +
            "appsink name=out sync=false max-buffers=200 drop=false",
            NullLogger.Instance);
        recording.UseTimeline(origin);
        recording.Play();
        Volatile.Write(ref microphone, recording);

        // The canceller needs a moment to find the echo path; what it lets through after that is measured.
        var levels = new List<double>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await Task.Run(
            () =>
            {
                while (watch.Elapsed < TimeSpan.FromSeconds(6))
                {
                    Samples.Pull(recording.Element("out"), (pcm, _) =>
                    {
                        if (watch.Elapsed > TimeSpan.FromSeconds(2))
                        {
                            levels.Add(VoiceActivityDetector.LevelDb(pcm));
                        }
                    });
                }
            },
            token);

        Volatile.Write(ref microphone, null);
        await stop.CancelAsync();
        await room;

        Assert.True(levels.Count > 200, $"The microphone pipeline produced sound ({levels.Count} blocks).");
        Assert.True(levels.Max() < -40, $"The echo is removed (the loudest block is {levels.Max():0.0} dBFS).");
    }

    /// <summary>
    /// Plays noise with a loudness that changes like speech and lets the microphone hear it after the room delay.
    /// The blocks carry even timestamps on the shared timeline, as the blocks of audio devices do.
    /// </summary>
    /// <param name="playback">The playback pipeline.</param>
    /// <param name="microphone">Returns the microphone pipeline once it runs.</param>
    /// <param name="origin">The moment both pipelines count time from.</param>
    /// <param name="cancellationToken">Stops the sound.</param>
    /// <returns>A task that completes when the sound is stopped.</returns>
    private static async Task PlayAndEchoAsync(GstPipeline playback, Func<GstPipeline?> microphone, ulong origin, CancellationToken cancellationToken)
    {
        var noise = new Random(7);
        var room = new Queue<byte[]>();
        var start = GstPipeline.SystemTime() - origin;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (long block = 0; !cancellationToken.IsCancellationRequested;)
        {
            for (; block * 10 < clock.ElapsedMilliseconds; block++)
            {
                var played = new byte[960];
                var loudness = 0.5 + (0.5 * Math.Sin(block / 37.0));
                for (var i = 0; i < played.Length; i += 2)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(played.AsSpan(i), (short)((noise.NextDouble() - 0.5) * 16000 * loudness));
                }

                var time = start + ((ulong)block * BLOCK_NANOSECONDS);
                Samples.Push(playback.Element("src"), played, time);
                room.Enqueue(played);
                if (room.Count > ROOM_DELAY_BLOCKS && room.Dequeue() is { } heard && microphone() is { } recording)
                {
                    Samples.Push(recording.Element("src"), heard, time);
                }
            }

            await Task.Delay(5, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Finds the bundle prepared in the repository.
    /// </summary>
    /// <returns>The bundle root or <see langword="null"/>.</returns>
    private static string? FindBundle()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Native", ComponentCatalog.CurrentRuntimeIdentifier, "gstreamer");
            if (GStreamerBundle.IsComplete(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
