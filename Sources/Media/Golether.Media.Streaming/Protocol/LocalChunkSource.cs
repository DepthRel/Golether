using Golether.Core.Media;

namespace Golether.Media.Streaming.Protocol;

/// <summary>
/// <see cref="IChunkSource"/> over a local file, used when a participant has the same file or in tests.
/// </summary>
public sealed class LocalChunkSource : IChunkSource
{
    /// <summary>
    /// The file.
    /// </summary>
    private readonly Stream _file;

    /// <summary>
    /// Serializes reads.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalChunkSource"/> class.
    /// </summary>
    /// <param name="media">The media.</param>
    /// <param name="file">A readable, seekable stream; ownership is transferred.</param>
    public LocalChunkSource(MediaDescriptor media, Stream file)
    {
        Media = media ?? throw new ArgumentNullException(nameof(media));
        _file = file ?? throw new ArgumentNullException(nameof(file));
    }

    /// <inheritdoc />
    public MediaDescriptor Media { get; }

    /// <inheritdoc />
    public async ValueTask<byte[]> GetChunkAsync(long index, CancellationToken cancellationToken)
    {
        var data = new byte[Media.GetChunkLength(index)];
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _file.Position = index * Media.ChunkSize;
            await _file.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false);
            return data;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _file.DisposeAsync();
}
