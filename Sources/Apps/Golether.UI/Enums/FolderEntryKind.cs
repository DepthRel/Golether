namespace Golether.UI.Enums;

/// <summary>
/// What an entry of the folder browser is.
/// </summary>
public enum FolderEntryKind
{
    /// <summary>
    /// A nested folder, opened with a click.
    /// </summary>
    Folder = 0,

    /// <summary>
    /// A picture, shown with its small picture.
    /// </summary>
    Picture = 1,

    /// <summary>
    /// A video.
    /// </summary>
    Video = 2,
}
