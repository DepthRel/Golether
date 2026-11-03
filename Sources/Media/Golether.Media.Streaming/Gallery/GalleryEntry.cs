namespace Golether.Media.Streaming.Gallery;

/// <summary>
/// Something a chosen folder holds: a file, or a nested folder.
/// </summary>
/// <param name="Name">The name, as the user knows it; the extension of a file tells what it is.</param>
/// <param name="Path">The path a file is opened by (see <see cref="Files.MediaFiles.OpenRead"/>); empty for a folder
/// and for a file that is never shown.</param>
/// <param name="Length">The size of a file; zero for a folder.</param>
/// <param name="Folder">The nested folder, or <see langword="null"/> for a file.</param>
public sealed record GalleryEntry(string Name, string Path, long Length, IGalleryDirectory? Folder);
