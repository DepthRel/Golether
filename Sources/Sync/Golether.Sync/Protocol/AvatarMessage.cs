using Golether.Core.Identity;

namespace Golether.Sync.Protocol;

/// <summary>
/// The picture a participant shows to the others while the camera is off. Participants send it to the host without
/// <paramref name="Sender"/>; the host sets the authenticated sender and relays it to everybody (including the
/// sender). An empty <paramref name="Image"/> clears the avatar back to the coloured initial.
/// </summary>
/// <param name="Image">The PNG bytes, or <see langword="null"/> to clear the avatar.</param>
/// <param name="Sender">The sender, set by the host only.</param>
public sealed record AvatarMessage(byte[]? Image, PeerId? Sender) : SessionMessage
{
    /// <summary>
    /// The largest avatar accepted, in bytes; it must also fit into <see cref="SessionMessageChannel.MaxMessageSize"/>
    /// once encoded as JSON (base64 grows it by about a third).
    /// </summary>
    public const int MaxImageBytes = 128 * 1024;

    /// <summary>
    /// The eight-byte signature every PNG file starts with.
    /// </summary>
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Validates a received avatar: an empty picture (a clear) is allowed, otherwise the bytes must be a PNG no larger
    /// than <see cref="MaxImageBytes"/>. The bytes come from another device, so they are never trusted on their shape.
    /// </summary>
    /// <returns>The message with a valid or cleared image, or <see langword="null"/> when it must be dropped.</returns>
    public AvatarMessage? Sanitize()
    {
        if (Image is null || Image.Length == 0)
        {
            return this with { Image = null };
        }

        return Image.Length <= MaxImageBytes && Image.AsSpan(0, Math.Min(Image.Length, PngSignature.Length)).SequenceEqual(PngSignature)
            ? this
            : null;
    }
}
