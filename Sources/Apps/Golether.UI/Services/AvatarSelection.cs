namespace Golether.UI.Services;

/// <summary>
/// The result of the crop view: the round avatar shown to others together with the original and its framing kept for
/// the next edit.
/// </summary>
/// <param name="Avatar">The round avatar (PNG) shown while the camera is off.</param>
/// <param name="Original">The original image bytes.</param>
/// <param name="Framing">The framing applied to the original.</param>
public sealed record AvatarSelection(byte[] Avatar, byte[] Original, AvatarFraming Framing);
