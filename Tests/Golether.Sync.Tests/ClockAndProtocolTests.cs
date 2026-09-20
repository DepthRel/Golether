using Golether.Core.Data.Enums;
using Golether.Core.Identity;
using Golether.Core.Media;
using Golether.Core.Playback;
using Golether.Core.Session;
using Golether.Sync.Clock;
using Golether.Sync.Protocol;

namespace Golether.Sync.Tests;

/// <summary>
/// Tests of <see cref="ClockOffsetEstimator"/> and the message serialization.
/// </summary>
public sealed class ClockAndProtocolTests
{
    /// <summary>
    /// With a symmetric path the offset is exact even with a round trip of more than one second.
    /// </summary>
    [Fact]
    public void Estimator_SymmetricHighLatency_IsExact()
    {
        var estimator = new ClockOffsetEstimator();
        const long offset = 5_000_000_000;

        // One-way delay 620 ms, host processing 2 ms.
        Assert.True(estimator.AddSample(1_000_000, 1_000_000 + offset + 620_000, 1_000_000 + offset + 622_000, 2_242_000));

        Assert.Equal(offset, estimator.OffsetMicroseconds);
        Assert.Equal(TimeSpan.FromMilliseconds(1240), estimator.RoundTrip);
        Assert.Equal(TimeSpan.FromMilliseconds(620), estimator.Uncertainty);
    }

    /// <summary>
    /// The sample with the smallest round trip determines the offset.
    /// </summary>
    [Fact]
    public void Estimator_PrefersFastestSample()
    {
        var estimator = new ClockOffsetEstimator();

        // Asymmetric slow sample: 1500 ms out, 100 ms back -> biased offset.
        estimator.AddSample(0, 1_500_000, 1_500_000, 1_600_000);
        var biased = estimator.OffsetMicroseconds;

        // Fast symmetric sample: 50 ms each way, true offset 0.
        estimator.AddSample(10_000_000, 10_050_000, 10_050_000, 10_100_000);

        Assert.NotEqual(0, biased);
        Assert.Equal(0, estimator.OffsetMicroseconds);
    }

    /// <summary>
    /// Inconsistent samples are ignored.
    /// </summary>
    [Fact]
    public void Estimator_IgnoresInconsistentSamples()
    {
        var estimator = new ClockOffsetEstimator();

        Assert.False(estimator.AddSample(10, 0, 0, 5));
        Assert.False(estimator.AddSample(0, 10, 5, 20));
        Assert.False(estimator.AddSample(0, 0, 100, 10));
        Assert.False(estimator.HasEstimate);
        Assert.Null(estimator.RoundTrip);
    }

    /// <summary>
    /// The session clock adds the offset to the local clock.
    /// </summary>
    [Fact]
    public void ParticipantClock_AppliesOffset()
    {
        var local = new ManualClock { NowMicroseconds = 1_000 };
        var estimator = new ClockOffsetEstimator();
        var clock = new ParticipantSessionClock(local, estimator);
        Assert.False(clock.IsSynchronized);

        estimator.AddSample(0, 500, 500, 0);

        Assert.True(clock.IsSynchronized);
        Assert.Equal(1_500, clock.NowMicroseconds);
    }

    /// <summary>
    /// Every message type survives serialization.
    /// </summary>
    [Fact]
    public void Messages_RoundTrip()
    {
        var peer = PeerId.Parse(new string('d', 64));
        var state = PlaybackState.Initial(peer, 42) with { Cause = PlaybackCause.Seek, Position = TimeSpan.FromSeconds(4363.5) };
        var media = new MediaDescriptor { FileName = "Dune.mkv", Length = 64_200_000_000, QuickId = new string('e', 64) };
        SessionMessage[] messages =
        [
            new HelloMessage(1, "Марина", "token"),
            new PendingApprovalMessage(),
            new WelcomeMessage("Вечер", [new ParticipantInfo(peer, "Вы", true)], state, media, 4),
            new RejectedMessage(RejectReason.SessionFull, "full"),
            new ClockPingMessage(1),
            new ClockPongMessage(1, 2, 3),
            new PlaybackRequestMessage(new PlaybackRequest(PlaybackRequestKind.Seek, TimeSpan.FromMinutes(72))),
            new PlaybackStateMessage(state),
            new StatusReportMessage(new ParticipantStatus { PeerId = peer, Drift = TimeSpan.FromMilliseconds(-40), RoundTripMilliseconds = 1240 }),
            new ParticipantsMessage([new ParticipantInfo(peer, "Вы", true)], []),
            new MediaChangedMessage(media),
            new ByeMessage("bye"),
            new ConferenceSignalMessage(peer, "offer", "v=0"),
        ];

        foreach (var message in messages)
        {
            var restored = SessionMessageChannel.Deserialize(SessionMessageChannel.Serialize(message));
            Assert.Equal(message.GetType(), restored.GetType());
            Assert.Equal(SessionMessageChannel.Serialize(message), SessionMessageChannel.Serialize(restored));
        }
    }

    /// <summary>
    /// Unknown types and malformed JSON are reported as invalid data.
    /// </summary>
    /// <param name="json">The payload.</param>
    [Theory]
    [InlineData("{\"$t\":\"unknown\"}")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("null")]
    public void Messages_RejectInvalidPayloads(string json)
        => Assert.Throws<InvalidDataException>(() => SessionMessageChannel.Deserialize(System.Text.Encoding.UTF8.GetBytes(json)));
}
