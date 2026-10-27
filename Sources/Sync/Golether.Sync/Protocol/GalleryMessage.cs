using Golether.Core.Media;

namespace Golether.Sync.Protocol;

/// <summary>
/// A page of the items (pictures and videos) the host shows, sent to participants that can show them. A folder of
/// many items goes in several pages of the same <paramref name="CatalogId"/>; an empty <paramref name="CatalogId"/>
/// means the host no longer shows a folder.
/// </summary>
/// <param name="CatalogId">The identifier of the showing: 32 hexadecimal digits, or empty when it ended.</param>
/// <param name="Title">The name of the folder.</param>
/// <param name="Total">The number of items in the whole showing.</param>
/// <param name="Offset">The index of the first item of this page.</param>
/// <param name="Items">The items of this page.</param>
public sealed record GalleryMessage(string CatalogId, string Title, int Total, int Offset, IReadOnlyList<GalleryItem> Items) : SessionMessage
{
    /// <summary>
    /// The most items in one page: with names of 255 characters and folders of 128, each character written as an
    /// escape of six bytes, a page still fits into <see cref="SessionMessageChannel.MAX_MESSAGE_SIZE"/>.
    /// </summary>
    public const int PAGE_SIZE = 50;

    /// <summary>
    /// Makes the message that ends the showing of pictures.
    /// </summary>
    /// <returns>The message.</returns>
    public static GalleryMessage Closed() => new(string.Empty, string.Empty, 0, 0, []);

    /// <summary>
    /// Checks whether an identifier of a showing is well formed.
    /// </summary>
    /// <param name="catalogId">The identifier.</param>
    /// <returns><see langword="true"/> for 32 hexadecimal digits.</returns>
    public static bool IsCatalogId(string? catalogId) => catalogId is { Length: 32 } && catalogId.All(char.IsAsciiHexDigit);

    /// <summary>
    /// Checks a page received from the host: the numbers must agree and every picture must be acceptable.
    /// </summary>
    /// <returns>The page with a normalized title, or <see langword="null"/> when it must be dropped.</returns>
    public GalleryMessage? Sanitize()
    {
        if (CatalogId is null || CatalogId.Length == 0)
        {
            return Closed();
        }

        if (!IsCatalogId(CatalogId) || Items is null || Total is < 1 or > GalleryItem.MAX_ITEMS || Offset < 0 ||
            Items.Count > PAGE_SIZE || Offset + Items.Count > Total || Items.Any(i => i is null || !i.IsValid()))
        {
            return null;
        }

        return this with { Title = MediaDescriptor.NormalizeFileName(Title) };
    }
}
