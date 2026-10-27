using Golether.Core.Enums;

namespace Golether.Media.Streaming.Gallery;

/// <summary>
/// A file of a folder the host is about to show.
/// </summary>
/// <param name="Path">The full path.</param>
/// <param name="Folder">The nested folder it lies in, relative to the shown folder; empty for the folder itself.</param>
/// <param name="Kind">What it is.</param>
/// <param name="Length">The size of the file.</param>
/// <param name="NeedsDecoding">Whether the picture is of a format participants cannot show, so the host turns it
/// into a JPEG first (HEIC, AVIF, TIFF).</param>
public sealed record GalleryFile(string Path, string Folder, GalleryItemKind Kind, long Length, bool NeedsDecoding);
