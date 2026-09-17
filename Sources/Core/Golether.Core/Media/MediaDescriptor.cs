namespace Golether.Core.Media;

/// <summary>
/// Describes the media file the host shares with the session.
/// </summary>
public sealed record MediaDescriptor
{
    /// <summary>
    /// The default chunk size: 4 MiB.
    /// </summary>
    public const int DefaultChunkSize = 4 * 1024 * 1024;

    /// <summary>
    /// The largest chunk size accepted from the network: 16 MiB.
    /// </summary>
    public const int MaxChunkSize = 16 * 1024 * 1024;

    /// <summary>
    /// Gets the file name shown to participants (no directory part).
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Gets the file length in bytes.
    /// </summary>
    public required long Length { get; init; }

    /// <summary>
    /// Gets the chunk size in bytes.
    /// </summary>
    public int ChunkSize { get; init; } = DefaultChunkSize;

    /// <summary>
    /// Gets the quick identifier: a SHA-256 over the length and samples of the file. Participants with a local file
    /// with the same identifier play it from disk.
    /// </summary>
    public required string QuickId { get; init; }

    /// <summary>
    /// Gets the number of chunks.
    /// </summary>
    public long ChunkCount => Length == 0 ? 0 : ((Length - 1) / ChunkSize) + 1;

    /// <summary>
    /// Validates a descriptor received from the network.
    /// </summary>
    /// <exception cref="ArgumentException">The descriptor is invalid.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(FileName) || FileName.Length > 255
            || FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || FileName.Contains('/') || FileName.Contains('\\'))
        {
            throw new ArgumentException("The media file name is invalid.", nameof(FileName));
        }

        if (Length < 0)
        {
            throw new ArgumentException("The media length must not be negative.", nameof(Length));
        }

        if (ChunkSize is < 64 * 1024 or > MaxChunkSize)
        {
            throw new ArgumentException("The chunk size is out of range.", nameof(ChunkSize));
        }

        if (QuickId is null || QuickId.Length != 64 || !QuickId.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException("The quick identifier is invalid.", nameof(QuickId));
        }
    }

    /// <summary>
    /// Returns the length of a chunk.
    /// </summary>
    /// <param name="index">The chunk index.</param>
    /// <returns>The number of bytes in the chunk.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is out of range.</exception>
    public int GetChunkLength(long index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ChunkCount);
        var start = index * ChunkSize;
        return (int)Math.Min(ChunkSize, Length - start);
    }
}
