using System.Text;
using Golether.Sync.Protocol;

namespace Golether.Sync.Tests;

/// <summary>
/// Tests of the stroke messages of the pen.
/// </summary>
public sealed class DrawProtocolTests
{
    /// <summary>
    /// Points are brought into the picture, and broken messages are dropped.
    /// </summary>
    [Fact]
    public void Points_AreCheckedAndClamped()
    {
        var message = DrawMessage.Create("AB12", StrokePhase.Start, [new StrokePoint(-0.5f, 2f), new StrokePoint(0.25f, 0.75f)])!;
        Assert.Equal(new StrokePoint(0, 1), message.Points[0]);
        Assert.Equal(new StrokePoint(0.25f, 0.75f), message.Points[1]);
        Assert.Null(message.Sender);

        Assert.Single(DrawMessage.Create("AB12", StrokePhase.Continue, [new StrokePoint(float.NaN, 0), new StrokePoint(0.5f, 0.5f)])!.Points);
        Assert.Null(DrawMessage.Create("AB12", StrokePhase.Continue, [new StrokePoint(float.NaN, 0)]));
        Assert.Null(DrawMessage.Create("zz", StrokePhase.Start, [new StrokePoint(0.5f, 0.5f)]));
        Assert.Null(DrawMessage.Create(string.Empty, StrokePhase.Start, [new StrokePoint(0.5f, 0.5f)]));
        Assert.Null(DrawMessage.Create(new string('A', DrawMessage.MaxIdLength + 1), StrokePhase.Start, [new StrokePoint(0.5f, 0.5f)]));
        Assert.Null(DrawMessage.Create("AB12", (StrokePhase)9, [new StrokePoint(0.5f, 0.5f)]));
        Assert.Null(DrawMessage.Create("AB12", StrokePhase.Start, new StrokePoint[DrawMessage.MaxPoints + 1]));
    }

    /// <summary>
    /// Only the end of a stroke may carry no points: it is what finishes the line.
    /// </summary>
    [Fact]
    public void EmptyMessage_IsOnlyAllowedAtTheEnd()
    {
        Assert.NotNull(DrawMessage.Create("AB12", StrokePhase.End, []));
        Assert.Null(DrawMessage.Create("AB12", StrokePhase.Start, []));
        Assert.Null(DrawMessage.Create("AB12", StrokePhase.Continue, []));
    }

    /// <summary>
    /// Every stroke gets its own identifier, made of hexadecimal characters.
    /// </summary>
    [Fact]
    public void StrokeIdentifiers_AreUnique()
    {
        var first = DrawMessage.CreateStrokeId();
        var second = DrawMessage.CreateStrokeId();
        Assert.NotEqual(first, second);
        Assert.Equal(24, first.Length);
        Assert.True(first.All(char.IsAsciiHexDigit));
    }

    /// <summary>
    /// A stroke message travels as its own kind of session message.
    /// </summary>
    [Fact]
    public void Message_RoundTrips()
    {
        SessionMessage message = DrawMessage.Create("AB12", StrokePhase.Continue, [new StrokePoint(0.5f, 0.25f)])!;
        var payload = SessionMessageChannel.Serialize(message);
        Assert.Contains("\"draw\"", Encoding.UTF8.GetString(payload), StringComparison.Ordinal);

        var back = Assert.IsType<DrawMessage>(SessionMessageChannel.Deserialize(payload));
        Assert.Equal("AB12", back.StrokeId);
        Assert.Equal(StrokePhase.Continue, back.Phase);
        Assert.Equal(new StrokePoint(0.5f, 0.25f), Assert.Single(back.Points));
    }

    /// <summary>
    /// The limiter lets a burst through and then holds the rest until the window slides on.
    /// </summary>
    [Fact]
    public void Limiter_HoldsBackABurst()
    {
        var time = new ManualTimeProvider();
        var limiter = new SlidingRateLimiter(TimeSpan.FromSeconds(1), 3);
        Assert.True(limiter.TryAcquire(time.GetTimestamp(), time));
        Assert.True(limiter.TryAcquire(time.GetTimestamp(), time));
        Assert.True(limiter.TryAcquire(time.GetTimestamp(), time));
        Assert.False(limiter.TryAcquire(time.GetTimestamp(), time));

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(limiter.TryAcquire(time.GetTimestamp(), time));
    }

    /// <summary>
    /// A time provider moved by the test.
    /// </summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        /// <summary>
        /// The current timestamp.
        /// </summary>
        private long _ticks = 1_000_000;

        /// <inheritdoc />
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        /// <inheritdoc />
        public override long GetTimestamp() => _ticks;

        /// <summary>
        /// Moves the time forward.
        /// </summary>
        /// <param name="value">The step.</param>
        public void Advance(TimeSpan value) => _ticks += value.Ticks;
    }
}
