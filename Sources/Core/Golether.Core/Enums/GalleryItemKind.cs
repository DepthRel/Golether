namespace Golether.Core.Enums;

/// <summary>
/// What an item of a shown folder is.
/// </summary>
public enum GalleryItemKind
{
    /// <summary>
    /// A picture: participants fetch it from the host and show it themselves.
    /// </summary>
    Picture = 0,

    /// <summary>
    /// A video: put on the screen, it becomes the film everybody watches together.
    /// </summary>
    Video = 1,
}
