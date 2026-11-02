namespace Golether.Client.Services;

/// <summary>
/// What the pictures of a shown folder need from a session.
/// </summary>
public interface IGallerySession
{
    /// <summary>
    /// Ends the showing of a folder, so everybody goes back to the film (host only).
    /// </summary>
    /// <returns>A task that completes when everybody was told.</returns>
    Task CloseGalleryAsync();

    /// <summary>
    /// Puts an item of the shown folder on everybody's screen (a video becomes the film); a participant asks the
    /// host, who decides.
    /// </summary>
    /// <param name="catalogId">The showing the item belongs to.</param>
    /// <param name="index">The index of the item.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the choice was sent.</returns>
    Task ShowItemAsync(string catalogId, int index, CancellationToken cancellationToken);

    /// <summary>
    /// Notes which picture this device looks at alone, so the others see where it is.
    /// </summary>
    /// <param name="index">The picture, or <see langword="null"/> when following the common screen.</param>
    void SetGalleryView(int? index);

    /// <summary>
    /// Returns the small picture of an item of the shown folder.
    /// </summary>
    /// <param name="catalogId">The showing the item belongs to.</param>
    /// <param name="index">The index of the item.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The JPEG bytes, or <see langword="null"/> when there is none.</returns>
    Task<byte[]?> LoadThumbnailAsync(string catalogId, int index, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the bytes of a picture of the shown folder.
    /// </summary>
    /// <param name="catalogId">The showing the picture belongs to.</param>
    /// <param name="index">The index of the picture.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bytes.</returns>
    Task<byte[]> LoadPictureAsync(string catalogId, int index, CancellationToken cancellationToken);
}
