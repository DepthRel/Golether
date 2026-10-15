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
    /// How far ahead of the player the media is read, in bytes, whatever the size of a piece.
    /// </summary>
    private const long READ_AHEAD_BYTES = 128L * 1024 * 1024;

    /// <summary>
    /// Returns the settings for a source that fetches over a given number of streams. One stream is left free of
    /// reading ahead whenever there is more than one: the piece the player waits for must never queue behind
    /// pieces nobody needs yet. That matters most right after opening and after a seek, on a slow connection.
    /// </summary>
    /// <param name="streams">The number of pieces the source fetches at once.</param>
    /// <param name="chunkSize">The size of a piece in bytes.</param>
    /// <returns>The settings.</returns>
    public static ReadAheadOptions For(int streams, int chunkSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(streams, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);
        return new ReadAheadOptions
        {
            BatchSize = Math.Max(1, streams - 1),
            ReadAheadChunks = (int)Math.Clamp(READ_AHEAD_BYTES / chunkSize, 4, 4096),
        };
    }

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
