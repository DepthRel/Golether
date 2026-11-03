namespace Golether.Media.Streaming.Gallery;

/// <summary>
/// A folder the host chose to show, wherever it lies: on the disk, or behind the system (a folder chosen on Android,
/// whose files are reached through the system rather than by name).
/// </summary>
public interface IGalleryDirectory
{
    /// <summary>
    /// Gets the name of the folder, as the user knows it.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Lists what the folder holds, in any order; hidden files and links are left out.
    /// </summary>
    /// <returns>The files and nested folders.</returns>
    /// <exception cref="IOException">The folder cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder may not be read.</exception>
    IReadOnlyList<GalleryEntry> List();
}
