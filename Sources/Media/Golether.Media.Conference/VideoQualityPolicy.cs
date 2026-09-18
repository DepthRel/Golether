using Golether.Core.Identity;

namespace Golether.Media.Conference;

/// <summary>
/// The camera quality sent to a participant changed.
/// </summary>
/// <param name="Peer">The participant.</param>
/// <param name="Quality">The new quality.</param>
public sealed record VideoQualityChange(PeerId Peer, VideoQuality Quality);

/// <summary>
/// The quality of the camera stream sent to one participant.
/// </summary>
public enum VideoQuality
{
    /// <summary>
    /// 640×360, 15 frames per second.
    /// </summary>
    High = 0,

    /// <summary>
    /// 320×180, 10 frames per second, about a quarter of the bitrate: for weak connections.
    /// </summary>
    Low = 1,
}

/// <summary>
/// Chooses the camera quality for one participant from the network statistics of the connection to them.
/// </summary>
/// <remarks>
/// Losses or a long round trip lower the quality quickly (two bad samples); a good connection restores it slowly
/// (five good samples), so the picture does not flicker between the two.
/// </remarks>
public sealed class VideoQualityPolicy
{
    /// <summary>
    /// The packet loss that counts as bad.
    /// </summary>
    public const double BadLoss = 0.05;

    /// <summary>
    /// The packet loss that counts as good.
    /// </summary>
    public const double GoodLoss = 0.01;

    /// <summary>
    /// The round trip that counts as bad, in seconds.
    /// </summary>
    public const double BadRoundTrip = 0.5;

    /// <summary>
    /// The round trip that counts as good, in seconds.
    /// </summary>
    public const double GoodRoundTrip = 0.3;

    /// <summary>
    /// Consecutive bad samples before lowering the quality.
    /// </summary>
    public const int SamplesToLower = 2;

    /// <summary>
    /// Consecutive good samples before raising the quality.
    /// </summary>
    public const int SamplesToRaise = 5;

    /// <summary>
    /// The consecutive bad samples.
    /// </summary>
    private int _bad;

    /// <summary>
    /// The consecutive good samples.
    /// </summary>
    private int _good;

    /// <summary>
    /// Gets the current quality.
    /// </summary>
    public VideoQuality Quality { get; private set; } = VideoQuality.High;

    /// <summary>
    /// Takes a statistics sample.
    /// </summary>
    /// <param name="fractionLost">The share of lost packets reported by the participant (0–1), when known.</param>
    /// <param name="roundTrip">The round trip in seconds, when known.</param>
    /// <returns><see langword="true"/> when the quality changed.</returns>
    public bool Update(double? fractionLost, double? roundTrip)
    {
        if (fractionLost is null && roundTrip is null)
        {
            return false;
        }

        var bad = fractionLost >= BadLoss || roundTrip >= BadRoundTrip;
        var good = (fractionLost ?? 0) <= GoodLoss && (roundTrip ?? 0) < GoodRoundTrip;
        _bad = bad ? _bad + 1 : 0;
        _good = good ? _good + 1 : 0;
        var next = Quality switch
        {
            VideoQuality.High when _bad >= SamplesToLower => VideoQuality.Low,
            VideoQuality.Low when _good >= SamplesToRaise => VideoQuality.High,
            _ => Quality,
        };
        if (next == Quality)
        {
            return false;
        }

        Quality = next;
        _bad = 0;
        _good = 0;
        return true;
    }

    /// <summary>
    /// Checks whether an encoded VP8 frame is a key frame, so a new stream can start with it.
    /// </summary>
    /// <param name="frame">The frame.</param>
    /// <returns><see langword="true"/> for a key frame.</returns>
    public static bool IsVp8KeyFrame(ReadOnlySpan<byte> frame)
        => frame.Length >= 10 && (frame[0] & 1) == 0 && frame[3] == 0x9D && frame[4] == 0x01 && frame[5] == 0x2A;
}
