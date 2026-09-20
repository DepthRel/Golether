using Golether.Core.Media;

namespace Golether.Media.Streaming.Protocol;

/// <summary>
/// <see cref="ISharedMedia"/> for a local file.
/// </summary>
public sealed class LocalSharedMedia : ISharedMedia
{
    /// <summary>
    /// The shared file and its descriptor.
    /// </summary>
    private volatile SharedFile? _current;

    /// <inheritdoc />
    public MediaDescriptor? Descriptor => _current?.Descriptor;

    /// <summary>
    /// Gets the path of the shared file, or <see langword="null"/>.
    /// </summary>
    public string? FilePath => _current?.Path;

    /// <summary>
    /// Shares a file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="descriptor">The descriptor of the file.</param>
    public void Share(string path, MediaDescriptor descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(descriptor);
        _current = new SharedFile(Path.GetFullPath(path), descriptor);
    }

    /// <summary>
    /// Stops sharing.
    /// </summary>
    public void Clear() => _current = null;

    /// <inheritdoc />
    public Stream OpenRead()
    {
        var current = _current ?? throw new InvalidOperationException("No media is shared.");
        return Files.MediaFiles.OpenRead(current.Path);
    }

    /// <summary>
    /// A shared file.
    /// </summary>
    /// <param name="Path">The full path.</param>
    /// <param name="Descriptor">The descriptor.</param>
    private sealed record SharedFile(string Path, MediaDescriptor Descriptor);
}
