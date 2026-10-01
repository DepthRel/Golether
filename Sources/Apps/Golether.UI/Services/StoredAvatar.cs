namespace Golether.UI.Services;

/// <summary>
/// The original image the user picked and how it was framed, so the crop view can be re-opened on the original rather
/// than on the already-cropped circle.
/// </summary>
/// <param name="Original">The original image bytes, exactly as they were picked.</param>
/// <param name="Framing">The framing applied to it.</param>
public sealed record StoredAvatar(byte[] Original, AvatarFraming Framing);
