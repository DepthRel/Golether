using Golether.Core.Identity;
using Golether.Media.Conference;
using Golether.Media.Player;
using Golether.UI.Controls;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of the voice activity detector and of the click classification of the video.
/// </summary>
public sealed class VoiceAndClickTests
{
    /// <summary>
    /// A participant.
    /// </summary>
    private static readonly PeerId Guest = PeerId.Parse(new string('b', 64));

    /// <summary>
    /// The level of generated audio follows its amplitude.
    /// </summary>
    [Fact]
    public void Level_FollowsAmplitude()
    {
        Assert.Equal(double.NegativeInfinity, VoiceActivityDetector.LevelDb(new byte[960]));
        Assert.Equal(-6.02, VoiceActivityDetector.LevelDb(Square(16384, 20)), 1);
        Assert.Equal(-59.9, VoiceActivityDetector.LevelDb(Square(33, 20)), 1);
        Assert.Equal(double.NegativeInfinity, VoiceActivityDetector.LevelDb([]));
    }

    /// <summary>
    /// Speech lights the indicator after the attack time; a pause shorter than the hold keeps it; silence ends it;
    /// a single click of noise does not light it.
    /// </summary>
    [Fact]
    public void Detector_UsesAttackAndHold()
    {
        var detector = new VoiceActivityDetector();
        var block = TimeSpan.FromMilliseconds(20);
        var loud = Square(8000, 20);
        var quiet = Square(20, 20);

        Assert.Null(detector.Process(Guest, loud, block, T(0)));
        Assert.Null(detector.Process(Guest, quiet, block, T(20)));
        Assert.Null(detector.Process(Guest, loud, block, T(40)));
        Assert.Null(detector.Process(Guest, loud, block, T(60)));
        Assert.Equal(new SpeakingChange(Guest, true), detector.Process(Guest, loud, block, T(80)));
        Assert.Null(detector.Process(Guest, loud, block, T(100)));

        Assert.Empty(detector.Expire(T(300)));
        Assert.Null(detector.Process(Guest, loud, block, T(320)));
        Assert.Empty(detector.Expire(T(700)));
        Assert.Equal([new SpeakingChange(Guest, false)], detector.Expire(T(800)));
        Assert.Empty(detector.Expire(T(900)));

        Assert.Null(detector.Process(default, loud, block, T(1000)));
        Assert.Null(detector.Forget(default));
        Assert.Null(detector.Forget(Guest));
    }

    /// <summary>
    /// A second press soon and near is a double click; late or far presses are single clicks; a triple press starts
    /// over.
    /// </summary>
    [Fact]
    public void Clicks_AreClassifiedLikeWindows()
    {
        var (first, remember) = VideoHost.ClassifyPress(null, 1000, 100, 100, 500, 4, 4);
        Assert.Equal(VideoPointerAction.Click, first);

        var (second, afterDouble) = VideoHost.ClassifyPress(remember, 1300, 101, 99, 500, 4, 4);
        Assert.Equal(VideoPointerAction.DoubleClick, second);
        Assert.Null(afterDouble);

        Assert.Equal(VideoPointerAction.Click, VideoHost.ClassifyPress(afterDouble, 1400, 101, 99, 500, 4, 4).Action);
        Assert.Equal(VideoPointerAction.Click, VideoHost.ClassifyPress(remember, 1600, 100, 100, 500, 4, 4).Action);
        Assert.Equal(VideoPointerAction.Click, VideoHost.ClassifyPress(remember, 1100, 140, 100, 500, 4, 4).Action);
        Assert.Equal(VideoPointerAction.Click, VideoHost.ClassifyPress(remember, 900, 100, 100, 500, 4, 4).Action);
    }

    /// <summary>
    /// Creates a square wave of 48 kHz mono 16-bit samples.
    /// </summary>
    /// <param name="amplitude">The amplitude.</param>
    /// <param name="milliseconds">The duration.</param>
    /// <returns>The samples.</returns>
    private static byte[] Square(short amplitude, int milliseconds)
    {
        var samples = new byte[48 * milliseconds * 2];
        for (var i = 0; i < samples.Length / 2; i++)
        {
            var value = (short)(i % 48 < 24 ? amplitude : -amplitude);
            BitConverter.TryWriteBytes(samples.AsSpan(i * 2), value);
        }

        return samples;
    }

    /// <summary>
    /// Returns a time.
    /// </summary>
    /// <param name="milliseconds">The milliseconds.</param>
    /// <returns>The time.</returns>
    private static TimeSpan T(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
}
