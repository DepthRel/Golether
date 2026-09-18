namespace Golether.Media.Conference.GStreamer.Tests;

/// <summary>
/// Tests of <see cref="VideoQualityPolicy"/>.
/// </summary>
public sealed class VideoQualityPolicyTests
{
    /// <summary>
    /// Two bad samples lower the quality; a single one does not.
    /// </summary>
    [Fact]
    public void BadConnection_LowersQuickly()
    {
        var policy = new VideoQualityPolicy();

        Assert.False(policy.Update(0.08, 0.05));
        Assert.False(policy.Update(0, 0.05));
        Assert.False(policy.Update(0, 0.7));
        Assert.True(policy.Update(0.02, 0.9));
        Assert.Equal(VideoQuality.Low, policy.Quality);
    }

    /// <summary>
    /// Five good samples in a row restore the quality; a medium sample restarts the count.
    /// </summary>
    [Fact]
    public void GoodConnection_RestoresSlowly()
    {
        var policy = new VideoQualityPolicy();
        policy.Update(0.1, null);
        policy.Update(0.1, null);
        Assert.Equal(VideoQuality.Low, policy.Quality);

        for (var i = 0; i < 4; i++)
        {
            Assert.False(policy.Update(0, 0.02));
        }

        Assert.False(policy.Update(0.03, 0.02));
        for (var i = 0; i < 4; i++)
        {
            Assert.False(policy.Update(0.005, null));
        }

        Assert.True(policy.Update(0, 0.1));
        Assert.Equal(VideoQuality.High, policy.Quality);
    }

    /// <summary>
    /// Missing statistics change nothing.
    /// </summary>
    [Fact]
    public void MissingStatistics_AreIgnored()
    {
        var policy = new VideoQualityPolicy();
        policy.Update(0.5, null);

        Assert.False(policy.Update(null, null));
        Assert.False(policy.Update(null, null));
        Assert.Equal(VideoQuality.High, policy.Quality);
        Assert.True(policy.Update(0.5, null));
    }

    /// <summary>
    /// Key frames are recognized by the VP8 frame tag and start code.
    /// </summary>
    [Fact]
    public void KeyFrames_AreRecognized()
    {
        byte[] key = [0x50, 0x2B, 0x01, 0x9D, 0x01, 0x2A, 0x80, 0x02, 0x68, 0x01];
        Assert.True(VideoQualityPolicy.IsVp8KeyFrame(key));
        Assert.False(VideoQualityPolicy.IsVp8KeyFrame([0x51, .. key[1..]]));
        Assert.False(VideoQualityPolicy.IsVp8KeyFrame(key[..6]));
        Assert.False(VideoQualityPolicy.IsVp8KeyFrame([.. key[..3], 0, 0, 0, .. key[6..]]));
    }
}
