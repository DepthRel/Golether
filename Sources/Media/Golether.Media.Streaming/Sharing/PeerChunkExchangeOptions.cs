namespace Golether.Media.Streaming.Sharing;

/// <summary>
/// Settings of <see cref="PeerChunkExchange"/>.
/// </summary>
public sealed record PeerChunkExchangeOptions
{
    /// <summary>
    /// Gets the payload size of one data message.
    /// </summary>
    public int FragmentSize { get; init; } = 16 * 1024;

    /// <summary>
    /// Gets how often the available chunks are announced.
    /// </summary>
    public TimeSpan AnnounceInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets how long an announcement stays valid.
    /// </summary>
    public TimeSpan AnnouncementLifetime { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets how long a transfer may make no progress.
    /// </summary>
    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets the number of chunks served to one participant at a time.
    /// </summary>
    public int UploadsPerPeer { get; init; } = 2;

    /// <summary>
    /// Gets the largest number of chunk ranges in one announcement.
    /// </summary>
    public int MaxRanges { get; init; } = 64;
}
