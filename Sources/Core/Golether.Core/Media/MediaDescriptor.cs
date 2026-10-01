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
    /// The longest file name.
    /// </summary>
    public const int MaxFileNameLength = 255;

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
    /// Makes a file name every device accepts: control characters and path separators become underscores and the
    /// name is cut to <see cref="MaxFileNameLength"/>. The name is only shown, never used as a path, and a name that
    /// is fine on the system of the host (a backslash on Linux, for example) must not be refused by the others.
    /// </summary>
    /// <param name="name">The file name without a directory.</param>
    /// <returns>The name to put into a descriptor.</returns>
    public static string NormalizeFileName(string? name)
    {
        var cleaned = new string((name ?? string.Empty).Select(c => IsForbiddenInName(c) ? '_' : c).ToArray()).Trim();
        if (cleaned.Length > MaxFileNameLength)
        {
            cleaned = cleaned[..MaxFileNameLength].TrimEnd();
        }

        return cleaned.Length == 0 ? "_" : cleaned;
    }

    /// <summary>
    /// Validates a descriptor received from the network.
    /// </summary>
    /// <exception cref="ArgumentException">The descriptor is invalid.</exception>
    public void Validate()
    {
        // The rule is the same on every system: the characters a file name may hold differ between Windows, Linux
        // and macOS, and a name the system of the host allows must not end the session on another one.
        if (string.IsNullOrWhiteSpace(FileName) || FileName.Length > MaxFileNameLength || FileName.Any(IsForbiddenInName))
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

    /// <summary>
    /// Checks whether a character may not appear in a shared file name on any system.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns><see langword="true"/> for control characters and path separators.</returns>
    private static bool IsForbiddenInName(char c) => char.IsControl(c) || c is '/' or '\\';
}
