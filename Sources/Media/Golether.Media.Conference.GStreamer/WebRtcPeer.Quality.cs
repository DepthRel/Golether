using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Golether.Core.Data.Enums;
using Golether.Media.Conference.GStreamer.Native;
using Microsoft.Extensions.Logging;

namespace Golether.Media.Conference.GStreamer;

/// <summary>
/// The adaptive camera quality of a peer: the connection statistics decide which encoded stream the peer gets.
/// </summary>
internal sealed partial class WebRtcPeer
{
    /// <summary>
    /// The name of the statistics the remote side reports about what it receives.
    /// </summary>
    private const string RemoteInboundType = "remote-inbound-rtp";

    /// <summary>
    /// The name of the same statistics in GStreamer releases before 1.24.
    /// </summary>
    private const string RemoteInboundStats = "rtp-remote-inbound-stream-stats";

    /// <summary>
    /// How long a statistics request may take.
    /// </summary>
    private static readonly TimeSpan StatsTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Decides the quality from the statistics.
    /// </summary>
    private readonly VideoQualityPolicy _qualityPolicy = new();

    /// <summary>
    /// The stream the peer gets (<see cref="VideoQuality"/>).
    /// </summary>
    private volatile int _videoQuality;

    /// <summary>
    /// Whether frames are skipped until a key frame of the newly chosen stream.
    /// </summary>
    private volatile bool _awaitingKeyFrame;

    /// <summary>
    /// Gets the camera quality sent to the peer.
    /// </summary>
    public VideoQuality VideoQuality => (VideoQuality)_videoQuality;

    /// <summary>
    /// Sends an encoded video frame of one of the streams; frames of the other stream are ignored.
    /// </summary>
    /// <param name="data">The VP8 frame.</param>
    /// <param name="caps">The caps of the encoder output.</param>
    /// <param name="quality">The stream of the frame.</param>
    public void PushVideo(ReadOnlySpan<byte> data, nint caps, VideoQuality quality)
    {
        if ((int)quality != _videoQuality)
        {
            return;
        }

        if (_awaitingKeyFrame)
        {
            // A decoder cannot continue a stream it has not seen: the switch happens on a key frame.
            if (!VideoQualityPolicy.IsVp8KeyFrame(data))
            {
                return;
            }

            _awaitingKeyFrame = false;
        }

        PushVideo(data, caps);
    }

    /// <summary>
    /// Reads the connection statistics and switches the camera stream when the connection changed.
    /// </summary>
    /// <returns><see langword="true"/> when the stream changed.</returns>
    public bool UpdateVideoQuality()
    {
        if (!_verified || _disposed)
        {
            return false;
        }

        var (loss, roundTrip) = ReadNetworkStats();
        if (!_qualityPolicy.Update(loss, roundTrip))
        {
            return false;
        }

        _awaitingKeyFrame = true;
        _videoQuality = (int)_qualityPolicy.Quality;
        _logger.LogInformation(
            "Camera stream for {Peer}: {Quality} (loss {Loss:P0}, round trip {RoundTrip:0.000} s)",
            Peer.ToShortString(), _qualityPolicy.Quality, loss ?? 0, roundTrip ?? 0);
        return true;
    }

    /// <summary>
    /// Switches the camera stream at the next key frame.
    /// </summary>
    /// <param name="quality">The stream.</param>
    internal void SetVideoQuality(VideoQuality quality)
    {
        if ((int)quality != _videoQuality)
        {
            _awaitingKeyFrame = true;
            _videoQuality = (int)quality;
        }
    }

    /// <summary>
    /// Asks webrtcbin for its statistics and takes the worst loss and round trip the remote side reports.
    /// </summary>
    /// <returns>The share of lost packets and the round trip in seconds, when reported.</returns>
    internal unsafe (double? Loss, double? RoundTrip) ReadNetworkStats()
    {
        nint promise;
        lock (_nativeGate)
        {
            if (_disposed)
            {
                return (null, null);
            }

            promise = Gst.PromiseNew();
            try
            {
                Signals.Emit(_pipeline.Element("webrtc"), "get-stats", SignalArg.Object(Gst.PadGetType(), 0), SignalArg.Boxed(Gst.PromiseGetType(), promise));
            }
            catch (GstException ex)
            {
                Gst.MiniObjectUnref(promise);
                _logger.LogDebug("Statistics of {Peer} are not available: {Error}", Peer.ToShortString(), ex.Message);
                return (null, null);
            }
        }

        try
        {
            // The wait runs aside, so a closing pipeline that never answers cannot block the caller.
            var wait = Task.Run(() => Gst.PromiseWait(promise));
            if (!wait.Wait(StatsTimeout))
            {
                Gst.PromiseInterrupt(promise);
                wait.Wait();
                return (null, null);
            }

            if (wait.Result != 2)
            {
                return (null, null);
            }

            var reply = Gst.PromiseGetReply(promise);
            if (reply == 0)
            {
                return (null, null);
            }

            var collector = new StatsCollector();
            var handle = GCHandle.Alloc(collector);
            try
            {
                Gst.StructureForeach(reply, &OnStatistic, GCHandle.ToIntPtr(handle));
            }
            finally
            {
                handle.Free();
            }

            return (collector.Loss, collector.RoundTrip);
        }
        finally
        {
            Gst.MiniObjectUnref(promise);
        }
    }

    /// <summary>
    /// Takes one entry of the statistics.
    /// </summary>
    /// <param name="field">The field quark.</param>
    /// <param name="value">The value.</param>
    /// <param name="data">The collector handle.</param>
    /// <returns>1 to continue.</returns>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int OnStatistic(uint field, GValue* value, nint data)
    {
        if (value == null || value->GType != Gst.StructureGetType()
            || GCHandle.FromIntPtr(data).Target is not StatsCollector collector)
        {
            return 1;
        }

        var structure = Gst.ValueGetStructure(value);
        var name = structure == 0 ? null : Marshal.PtrToStringUTF8(Gst.StructureGetName(structure));
        if (name is null || !(name == RemoteInboundType || name.StartsWith(RemoteInboundStats, StringComparison.Ordinal)))
        {
            return 1;
        }

        if (Gst.StructureGetDouble(structure, "fraction-lost", out var loss) != 0 && double.IsFinite(loss))
        {
            collector.Loss = Math.Max(collector.Loss ?? 0, Math.Clamp(loss, 0, 1));
        }

        if (Gst.StructureGetDouble(structure, "round-trip-time", out var roundTrip) != 0 && double.IsFinite(roundTrip) && roundTrip > 0)
        {
            collector.RoundTrip = Math.Max(collector.RoundTrip ?? 0, roundTrip);
        }

        return 1;
    }

    /// <summary>
    /// The values gathered from the statistics.
    /// </summary>
    private sealed class StatsCollector
    {
        /// <summary>
        /// Gets or sets the worst loss.
        /// </summary>
        public double? Loss { get; set; }

        /// <summary>
        /// Gets or sets the longest round trip.
        /// </summary>
        public double? RoundTrip { get; set; }
    }
}
