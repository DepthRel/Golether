using Golether.Core.Identity;
using Golether.Sync.Protocol;

namespace Golether.Sync.Tests;

/// <summary>
/// Tests of the avatar message.
/// </summary>
public sealed class AvatarProtocolTests
{
    /// <summary>
    /// The eight-byte PNG signature followed by some payload.
    /// </summary>
    private static byte[] Png(int length)
    {
        var bytes = new byte[length];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes.AsSpan());
        return bytes;
    }

    /// <summary>
    /// A PNG within the size limit passes; an empty picture clears; oversized or non-PNG bytes are dropped.
    /// </summary>
    [Fact]
    public void Sanitize_ChecksPngAndSize()
    {
        var sender = PeerId.Parse(new string('a', 64));
        var good = new AvatarMessage(Png(64), sender);
        Assert.Equal(good, good.Sanitize());

        // An empty or missing picture is a clear, kept as null.
        Assert.Null(new AvatarMessage([], sender).Sanitize()!.Image);
        Assert.Null(new AvatarMessage(null, sender).Sanitize()!.Image);

        // Too large, or not a PNG: dropped.
        Assert.Null(new AvatarMessage(Png(AvatarMessage.MaxImageBytes + 1), sender).Sanitize());
        Assert.Null(new AvatarMessage([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], sender).Sanitize());
    }

    /// <summary>
    /// The message survives the wire format with its bytes and sender.
    /// </summary>
    [Fact]
    public void Message_RoundTrips()
    {
        var message = new AvatarMessage(Png(48), PeerId.Parse(new string('b', 64)));
        var payload = SessionMessageChannel.Serialize(message);
        Assert.Contains("\"avatar\"", System.Text.Encoding.UTF8.GetString(payload), StringComparison.Ordinal);

        var restored = Assert.IsType<AvatarMessage>(SessionMessageChannel.Deserialize(payload));
        Assert.Equal(message.Sender, restored.Sender);
        Assert.Equal(message.Image, restored.Image);
    }

    /// <summary>
    /// A small file may declare a picture of thousands of megapixels: the size in the PNG header is checked, so such
    /// a picture is never handed to a decoder.
    /// </summary>
    [Fact]
    public void Sanitize_RefusesPicturesTooLargeToDecode()
    {
        var sender = PeerId.Parse(new string('a', 64));

        Assert.NotNull(new AvatarMessage(PngWithSize(256, 256), sender).Sanitize());
        Assert.NotNull(new AvatarMessage(PngWithSize(AvatarMessage.MaxDimension, AvatarMessage.MaxDimension), sender).Sanitize());
        Assert.Null(new AvatarMessage(PngWithSize(AvatarMessage.MaxDimension + 1, 16), sender).Sanitize());
        Assert.Null(new AvatarMessage(PngWithSize(16, 60000), sender).Sanitize());
        Assert.Null(new AvatarMessage(PngWithSize(0, 16), sender).Sanitize());
        Assert.Null(new AvatarMessage(PngWithSize(uint.MaxValue, uint.MaxValue), sender).Sanitize());
    }

    /// <summary>
    /// Builds the start of a PNG file: the signature and a header chunk declaring a size.
    /// </summary>
    /// <param name="width">The declared width.</param>
    /// <param name="height">The declared height.</param>
    /// <returns>The bytes.</returns>
    private static byte[] PngWithSize(uint width, uint height)
    {
        var bytes = Png(64);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }
}
