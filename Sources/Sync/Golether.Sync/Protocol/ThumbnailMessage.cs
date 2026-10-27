namespace Golether.Sync.Protocol;

/// <summary>
/// A small picture of the folder strip. A participant asks for it without <paramref name="Image"/>; the host answers
/// with a JPEG, or without one when the picture cannot be read.
/// </summary>
/// <param name="CatalogId">The showing the picture belongs to.</param>
/// <param name="Index">The index of the picture.</param>
/// <param name="Image">The JPEG bytes, or <see langword="null"/>.</param>
public sealed record ThumbnailMessage(string CatalogId, int Index, byte[]? Image) : SessionMessage
{
    /// <summary>
    /// The largest small picture, in bytes: one of 320 pixels a side takes a few tens of kilobytes.
    /// </summary>
    public const int MAX_IMAGE_BYTES = 96 * 1024;

    /// <summary>
    /// The longest side of a small picture, in pixels.
    /// </summary>
    public const int MAX_DIMENSION = 320;

    /// <summary>
    /// Checks an answer of the host: a JPEG of an acceptable size, or nothing.
    /// </summary>
    /// <returns>The answer, with the picture dropped when it is not acceptable.</returns>
    public ThumbnailMessage Sanitize()
        => Image is { Length: > 3 and <= MAX_IMAGE_BYTES } image && image[0] == 0xFF && image[1] == 0xD8 && image[2] == 0xFF
            ? this
            : this with { Image = null };
}
