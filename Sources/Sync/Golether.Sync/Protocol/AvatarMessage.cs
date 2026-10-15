using System.Buffers.Binary;
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
    /// The largest avatar accepted, in bytes; it must also fit into <see cref="SessionMessageChannel.MAX_MESSAGE_SIZE"/>
    /// once encoded as JSON (base64 grows it by about a third).
    /// </summary>
    public const int MAX_IMAGE_BYTES = 128 * 1024;

    /// <summary>
    /// The largest width and height of an avatar, in pixels. A PNG of a few kilobytes may declare a picture of
    /// thousands of megapixels, and decoding it would take all the memory of everybody it is shown to.
    /// </summary>
    public const int MAX_DIMENSION = 1024;

    /// <summary>
    /// The eight-byte signature every PNG file starts with.
    /// </summary>
    private static readonly byte[] _pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Validates a received avatar: an empty picture (a clear) is allowed, otherwise the bytes must be a PNG no larger
    /// than <see cref="MAX_IMAGE_BYTES"/> whose header declares no more than <see cref="MAX_DIMENSION"/> pixels a side.
    /// The bytes come from another device, so they are never trusted on their shape.
    /// </summary>
    /// <returns>The message with a valid or cleared image, or <see langword="null"/> when it must be dropped.</returns>
    public AvatarMessage? Sanitize()
    {
        if (Image is null || Image.Length == 0)
        {
            return this with { Image = null };
        }

        return Image.Length <= MAX_IMAGE_BYTES &&
               Image.AsSpan(0, Math.Min(Image.Length, _pngSignature.Length)).SequenceEqual(_pngSignature) &&
               HasAcceptableSize(Image)
            ? this
            : null;
    }

    /// <summary>
    /// Reads the size a PNG declares in its header and checks it against <see cref="MAX_DIMENSION"/>. The header
    /// chunk (IHDR) is always the first one; bytes without it are not a picture any decoder would expand, so they
    /// are let through to fail harmlessly when decoded.
    /// </summary>
    /// <param name="png">The bytes, starting with the PNG signature.</param>
    /// <returns><see langword="false"/> when the declared picture is empty or too large.</returns>
    private static bool HasAcceptableSize(ReadOnlySpan<byte> png)
    {
        // Signature (8), chunk length (4), chunk type (4), then the width and the height, big-endian.
        const int HEADER_END = 24;
        if (png.Length < HEADER_END || !png.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return true;
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(png[16..]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(png[20..]);
        return width is >= 1 and <= MAX_DIMENSION && height is >= 1 and <= MAX_DIMENSION;
    }
}
