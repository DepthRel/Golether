using Golether.Core.Identity;
using Golether.Core.Media;

namespace Golether.Sync.Protocol;

/// <summary>
/// The item (a picture or a video) on the screen of everybody. A participant sends it to the host as an intention
/// (without <paramref name="Sender"/> and <paramref name="Version"/>); the host decides, numbers it and sends it to
/// everybody with the authenticated sender. The latest version wins, like a seek of the film.
/// </summary>
/// <param name="CatalogId">The showing the item belongs to.</param>
/// <param name="Index">The index of the item.</param>
/// <param name="Version">The number of the decision, set by the host only.</param>
/// <param name="Sender">Who chose the item, set by the host only.</param>
public sealed record GalleryShowMessage(string CatalogId, int Index, long Version, PeerId? Sender) : SessionMessage
{
    /// <summary>
    /// Checks the message against the showing it is meant for.
    /// </summary>
    /// <param name="catalogId">The current showing.</param>
    /// <param name="count">The number of its items.</param>
    /// <returns><see langword="true"/> when the message names an item of that showing.</returns>
    public bool Fits(string? catalogId, int count)
        => catalogId is not null && GalleryMessage.IsCatalogId(CatalogId) && string.Equals(CatalogId, catalogId, StringComparison.Ordinal) &&
           Index >= 0 && Index < Math.Min(count, GalleryItem.MAX_ITEMS);
}
