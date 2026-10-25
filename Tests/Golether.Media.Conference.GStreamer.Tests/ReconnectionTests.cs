namespace Golether.Media.Conference.GStreamer.Tests;

/// <summary>
/// Tests of how a failed media connection is told apart and made again, without GStreamer.
/// </summary>
public sealed class ReconnectionTests
{
    /// <summary>
    /// The session identifier is read from the origin line, whatever the line breaks.
    /// </summary>
    [Fact]
    public void ParseSession_ReadsTheOriginLine()
    {
        Assert.Equal("4611731400430051336", WebRtcPeer.ParseSession("v=0\r\no=- 4611731400430051336 2 IN IP4 127.0.0.1\r\ns=-\r\nm=video 9 UDP/TLS/RTP/SAVPF 96\r\n"));
        Assert.Equal("17", WebRtcPeer.ParseSession("v=0\no=user 17 0 IN IP4 0.0.0.0\ns=-\nm=audio 9 UDP/TLS/RTP/SAVPF 97\n"));
    }

    /// <summary>
    /// An SDP without a valid origin line has no session identifier.
    /// </summary>
    /// <param name="sdp">The SDP.</param>
    [Theory]
    [InlineData("v=0\r\ns=-\r\nm=video 9 UDP/TLS/RTP/SAVPF 96\r\n")]
    [InlineData("v=0\r\no=- abc 2 IN IP4 127.0.0.1\r\nm=video 9 UDP/TLS/RTP/SAVPF 96\r\n")]
    [InlineData("v=0\r\na=o=- 1 2 IN IP4 127.0.0.1\r\nm=video 9 UDP/TLS/RTP/SAVPF 96\r\n")]
    public void ParseSession_WithoutOrigin_IsNull(string sdp) => Assert.Null(WebRtcPeer.ParseSession(sdp));

    /// <summary>
    /// The pause before a connection is made again starts short and doubles up to a limit.
    /// </summary>
    [Fact]
    public void RestartDelay_GrowsUpToALimit()
    {
        int[] expected = [3, 6, 12, 24, 30, 30];
        Assert.Equal(expected, Enumerable.Range(0, expected.Length).Select(a => (int)GStreamerConferenceMedia.RestartDelay(a).TotalSeconds).ToArray());
        Assert.Equal(TimeSpan.FromSeconds(30), GStreamerConferenceMedia.RestartDelay(int.MaxValue));
        Assert.Equal(TimeSpan.FromSeconds(3), GStreamerConferenceMedia.RestartDelay(-1));
    }
}
