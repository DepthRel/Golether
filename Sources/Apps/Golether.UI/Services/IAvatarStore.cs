namespace Golether.UI.Services;

/// <summary>
/// Keeps this device's avatar in the application data folder: the round avatar shown in calls, plus the original image
/// and its framing so the user can re-position it later instead of starting from the cropped circle.
/// </summary>
public interface IAvatarStore
{
    /// <summary>
    /// Reads the current round avatar shown in calls.
    /// </summary>
    /// <returns>The PNG bytes, or <see langword="null"/> when no avatar was chosen.</returns>
    Task<byte[]?> LoadCurrentAsync();

    /// <summary>
    /// Reads the original image and its framing, so the crop view can re-open on the original.
    /// </summary>
    /// <returns>The original and framing, or <see langword="null"/> when none was stored.</returns>
    Task<StoredAvatar?> LoadForEditingAsync();

    /// <summary>
    /// Stores the chosen avatar: the round picture, the original and its framing.
    /// </summary>
    /// <param name="selection">The chosen avatar.</param>
    /// <returns>A task that completes when everything is stored.</returns>
    Task SaveAsync(AvatarSelection selection);

    /// <summary>
    /// Removes the stored avatar and its original, so the coloured initial is shown again.
    /// </summary>
    /// <returns>A task that completes when everything is removed.</returns>
    Task ClearAsync();
}
