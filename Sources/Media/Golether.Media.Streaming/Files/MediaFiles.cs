using System.Buffers.Binary;
using System.Security.Cryptography;
using Golether.Core.Media;

namespace Golether.Media.Streaming.Files;

/// <summary>
/// Describes local media files.
/// </summary>
public static class MediaFiles
{
    /// <summary>
    /// The size of each sample used by the quick identifier: 1 MiB.
    /// </summary>
    public const int SampleSize = 1024 * 1024;

    /// <summary>
    /// Computes the quick identifier of a file: SHA-256 over the length and three 1 MiB samples (start, middle, end).
    /// </summary>
    /// <param name="stream">A readable, seekable stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The lowercase hexadecimal identifier.</returns>
    /// <remarks>
    /// The identifier finds the same file among the participants without hashing tens of gigabytes. It is not a
    /// security boundary: chunks received from the network are verified separately.
    /// </remarks>
    public static async Task<string> ComputeQuickIdAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek || !stream.CanRead)
        {
            throw new ArgumentException("The stream must be readable and seekable.", nameof(stream));
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var length = stream.Length;
        var header = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(header, length);
        hash.AppendData(header);

        var buffer = new byte[SampleSize];
        foreach (var offset in SampleOffsets(length))
        {
            stream.Position = offset;
            var read = await stream.ReadAtLeastAsync(buffer, (int)Math.Min(SampleSize, length - offset), throwOnEndOfStream: false, cancellationToken)
                .ConfigureAwait(false);
            hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// Describes a local file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The descriptor.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public static async Task<MediaDescriptor> DescribeAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = OpenRead(path);
        return new MediaDescriptor
        {
            FileName = Path.GetFileName(path),
            Length = stream.Length,
            QuickId = await ComputeQuickIdAsync(stream, cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>
    /// Opens a file for shared, random-access reading.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The stream.</returns>
    public static FileStream OpenRead(string path)
        => new(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite | FileShare.Delete,
            Options = FileOptions.Asynchronous | FileOptions.RandomAccess,
            BufferSize = 0,
        });

    /// <summary>
    /// Returns the distinct sample offsets of a file.
    /// </summary>
    /// <param name="length">The file length.</param>
    /// <returns>The offsets in ascending order.</returns>
    private static IEnumerable<long> SampleOffsets(long length)
    {
        if (length == 0)
        {
            return [];
        }

        var middle = Math.Max(0, (length / 2) - (SampleSize / 2));
        var end = Math.Max(0, length - SampleSize);
        return new[] { 0L, middle, end }.Distinct();
    }
}
