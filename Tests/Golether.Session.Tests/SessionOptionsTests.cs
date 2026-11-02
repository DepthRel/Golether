using Golether.Media.Streaming.Caching;

namespace Golether.Session.Tests;

/// <summary>
/// Tests of <see cref="SessionOptions"/>: a phone keeps less of the film in memory than a computer.
/// </summary>
public sealed class SessionOptionsTests
{
    /// <summary>
    /// Without a limit of its own a client keeps as much as a computer does.
    /// </summary>
    [Fact]
    public void MediaCache_ByDefault_IsTheCapacityOfTheCache() => Assert.Equal(ChunkCache.DEFAULT_CAPACITY, new SessionOptions().MediaCacheCapacity);

    /// <summary>
    /// A cache without room is a mistake, not a choice: nothing could be played.
    /// </summary>
    [Fact]
    public void Validate_RefusesAnEmptyMediaCache()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new SessionOptions { MediaCacheCapacity = 0 }.Validate());
}
