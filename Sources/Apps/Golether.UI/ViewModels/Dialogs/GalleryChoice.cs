using Golether.Media.Streaming.Gallery;

namespace Golether.UI.ViewModels.Dialogs;

/// <summary>
/// The folder the host chose to show, and how.
/// </summary>
/// <param name="Folder">What of the folder is shown.</param>
/// <param name="StripMetadata">Whether the place, the camera and the comments are left out of the pictures.</param>
public sealed record GalleryChoice(GalleryFolder Folder, bool StripMetadata);
