namespace Golether.UI.Services;

/// <summary>
/// How the chosen image is framed inside the round avatar: the values fed to the crop view so re-opening it shows the
/// same position.
/// </summary>
/// <param name="Zoom">The zoom applied to the image.</param>
/// <param name="OffsetX">The horizontal shift, in pixels of the crop frame.</param>
/// <param name="OffsetY">The vertical shift, in pixels of the crop frame.</param>
public sealed record AvatarFraming(double Zoom, double OffsetX, double OffsetY);

/// <summary>
/// The original image the user picked and how it was framed, so the crop view can be re-opened on the original rather
/// than on the already-cropped circle.
/// </summary>
/// <param name="Original">The original image bytes, exactly as they were picked.</param>
/// <param name="Framing">The framing applied to it.</param>
public sealed record StoredAvatar(byte[] Original, AvatarFraming Framing);

/// <summary>
/// The result of the crop view: the round avatar shown to others together with the original and its framing kept for
/// the next edit.
/// </summary>
/// <param name="Avatar">The round avatar (PNG) shown while the camera is off.</param>
/// <param name="Original">The original image bytes.</param>
/// <param name="Framing">The framing applied to the original.</param>
public sealed record AvatarSelection(byte[] Avatar, byte[] Original, AvatarFraming Framing);

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
