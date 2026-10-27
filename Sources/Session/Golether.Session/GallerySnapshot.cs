using Golether.Core.Identity;
using Golether.Core.Media;

namespace Golether.Session;

/// <summary>
/// The folder shown in the session: its items (pictures and videos) and the one on everybody's screen.
/// </summary>
public sealed record GallerySnapshot
{
    /// <summary>
    /// Gets the identifier of the showing.
    /// </summary>
    public required string CatalogId { get; init; }

    /// <summary>
    /// Gets the name of the folder.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the number of items in the showing.
    /// </summary>
    public required int Total { get; init; }

    /// <summary>
    /// Gets the items received so far, by index; a participant receives a large folder in pages.
    /// </summary>
    public required IReadOnlyList<GalleryItem?> Items { get; init; }

    /// <summary>
    /// Gets the index of the item on everybody's screen.
    /// </summary>
    public required int Stage { get; init; }

    /// <summary>
    /// Gets the number of the decision that put the item there; it grows with every change.
    /// </summary>
    public required long StageVersion { get; init; }

    /// <summary>
    /// Gets who chose the item, or <see langword="null"/> when it is the first one of the showing.
    /// </summary>
    public PeerId? StageOrigin { get; init; }
}
