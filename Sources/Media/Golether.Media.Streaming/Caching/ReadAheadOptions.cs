namespace Golether.Media.Streaming.Caching;

/// <summary>
/// Settings of read-ahead.
/// </summary>
public sealed record ReadAheadOptions
{
    /// <summary>
    /// Gets the number of chunks fetched ahead of the read position (default 32, that is 128 MiB with 4 MiB chunks).
    /// </summary>
    public int ReadAheadChunks { get; init; } = 32;

    /// <summary>
    /// Gets the number of chunks fetched at once by the read-ahead loop (default 4).
    /// </summary>
    public int BatchSize { get; init; } = 4;

    /// <summary>
    /// Validates the options.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ReadAheadChunks);
        ArgumentOutOfRangeException.ThrowIfLessThan(BatchSize, 1);
    }
}
