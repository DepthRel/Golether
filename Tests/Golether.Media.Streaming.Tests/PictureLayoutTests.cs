using System.Buffers.Binary;
using System.Text;
using Golether.Media.Streaming.Gallery;

namespace Golether.Media.Streaming.Tests;

/// <summary>
/// Tests of the pictures as participants receive them: the metadata is left out, the picture itself is untouched.
/// </summary>
public sealed class PictureLayoutTests : IDisposable
{
    /// <summary>
    /// The temporary picture file.
    /// </summary>
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"golether-picture-{Guid.NewGuid():N}");

    /// <inheritdoc />
    public void Dispose() => File.Delete(_path);

    /// <summary>
    /// A JPEG loses its EXIF block, comments and foreign APP2 blocks, keeps the JFIF block, the colour profile and
    /// the picture data, and gets an EXIF block with the orientation only.
    /// </summary>
    [Fact]
    public void Jpeg_LosesMetadataButKeepsOrientationAndPicture()
    {
        var scan = new byte[] { 0x11, 0x22, 0xFF, 0x00, 0x33, 0xFF, 0xD9 };
        var jpeg = Concat(
            [0xFF, 0xD8],
            Segment(0xE0, Encoding.ASCII.GetBytes("JFIF\0\x01\x01\0\0\x01\0\x01\0\0")),
            Segment(0xE1, Exif(orientation: 6)),
            Segment(0xFE, Encoding.ASCII.GetBytes("secret comment")),
            Segment(0xE2, Encoding.ASCII.GetBytes("ICC_PROFILE\0\x01\x01profile")),
            Segment(0xE2, Encoding.ASCII.GetBytes("MPF\0multi-picture")),
            Segment(0xDB, new byte[65]),
            Segment(0xDA, new byte[10]),
            scan);

        var result = Strip(jpeg);

        var text = Encoding.ASCII.GetString(result);
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GPSDATA", text, StringComparison.Ordinal);
        Assert.DoesNotContain("multi-picture", text, StringComparison.Ordinal);
        Assert.Contains("JFIF", text, StringComparison.Ordinal);
        Assert.Contains("ICC_PROFILE", text, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF, 0xE1 }, result[..4]);

        // The orientation entry: tag 0x0112, SHORT, one value, 6.
        Assert.True(result.AsSpan().IndexOf(new byte[] { 0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, 0x06 }) > 0);
        Assert.Equal(scan, result[^scan.Length..]);
    }

    /// <summary>
    /// A JPEG photographed upright gets no orientation block at all.
    /// </summary>
    [Fact]
    public void Jpeg_UprightGetsNoExifBlock()
    {
        var jpeg = Concat([0xFF, 0xD8], Segment(0xE1, Exif(orientation: 1)), Segment(0xDA, new byte[10]), [0x01, 0xFF, 0xD9]);

        var result = Strip(jpeg);

        Assert.Equal(Concat([0xFF, 0xD8], Segment(0xDA, new byte[10]), [0x01, 0xFF, 0xD9]), result);
    }

    /// <summary>
    /// Whatever follows the end of a JPEG picture is left out: a second picture of the camera with its own EXIF block
    /// (MPF), a short video of a "live" photo. Stuffed bytes and restart markers inside the scan stay.
    /// </summary>
    [Fact]
    public void Jpeg_LosesWhatFollowsTheEndOfThePicture()
    {
        var scan = new byte[] { 0x11, 0xFF, 0x00, 0x22, 0xFF, 0xD0, 0x33, 0xFF, 0xFF, 0xD1, 0x44, 0xFF, 0xD9 };
        var primary = Concat([0xFF, 0xD8], Segment(0xDB, new byte[65]), Segment(0xDA, new byte[10]), scan);
        var second = Concat([0xFF, 0xD8], Segment(0xE1, Encoding.ASCII.GetBytes("Exif\0\0GPS-OF-THE-SECOND")), Segment(0xDA, new byte[10]), [0x55, 0xFF, 0xD9]);
        var video = Concat(Encoding.ASCII.GetBytes("ftypmp42"), new byte[300], Encoding.ASCII.GetBytes("udta-location"));

        var result = Strip(Concat(primary, second, video));

        Assert.Equal(primary, result);
    }

    /// <summary>
    /// A JPEG of several scans keeps the tables between them, even when their bytes look like an end marker, and loses
    /// a metadata block placed between them.
    /// </summary>
    [Fact]
    public void Jpeg_LosesMetadataBetweenScans()
    {
        var table = Segment(0xC4, [0x10, 0xFF, 0xD9, 0x01, 0x02]);
        var jpeg = Concat(
            [0xFF, 0xD8],
            Segment(0xDA, new byte[10]),
            [0x01, 0x02, 0xFF, 0x00],
            table,
            Segment(0xE1, Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0secret place")),
            Segment(0xDA, new byte[10]),
            [0x03, 0x04, 0xFF, 0xD9]);

        var result = Strip(jpeg);

        Assert.DoesNotContain("secret", Encoding.ASCII.GetString(result), StringComparison.Ordinal);
        Assert.Equal(Concat([0xFF, 0xD8], Segment(0xDA, new byte[10]), [0x01, 0x02, 0xFF, 0x00], table, Segment(0xDA, new byte[10]), [0x03, 0x04, 0xFF, 0xD9]), result);
    }

    /// <summary>
    /// A GIF loses its comments and the application blocks of metadata (XMP), keeps the loop of the animation, the
    /// timing of its frames and the frames, and nothing after its end.
    /// </summary>
    [Fact]
    public void Gif_LosesCommentsAndForeignBlocks()
    {
        var head = Concat(Encoding.ASCII.GetBytes("GIF89a"), [0x02, 0x00, 0x02, 0x00, 0x80, 0x00, 0x00], [0, 0, 0, 255, 255, 255]);
        var loop = Concat([0x21, 0xFF, 0x0B], Encoding.ASCII.GetBytes("NETSCAPE2.0"), [0x03, 0x01, 0x00, 0x00, 0x00]);
        var comment = Concat([0x21, 0xFE, 0x0C], Encoding.ASCII.GetBytes("secret place"), [0x00]);
        var xmp = Concat([0x21, 0xFF, 0x0B], Encoding.ASCII.GetBytes("XMP DataXMP"), [0x05], Encoding.ASCII.GetBytes("GPS:1"), [0x00]);
        var timing = new byte[] { 0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00 };
        var frame = new byte[] { 0x2C, 0, 0, 0, 0, 0x02, 0x00, 0x02, 0x00, 0x00, 0x02, 0x02, 0x4C, 0x01, 0x00 };
        var gif = Concat(head, loop, comment, xmp, timing, frame, [0x3B], Encoding.ASCII.GetBytes("appended"));

        var result = Strip(gif);

        Assert.Equal(Concat(head, loop, timing, frame, [0x3B]), result);
    }

    /// <summary>
    /// A PNG loses its text and EXIF chunks and what follows its end, and keeps the rest in order.
    /// </summary>
    [Fact]
    public void Png_LosesTextChunks()
    {
        var header = Chunk("IHDR", new byte[13]);
        var data = Chunk("IDAT", [1, 2, 3, 4, 5]);
        var end = Chunk("IEND", []);
        var png = Concat(
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A],
            header,
            Chunk("tEXt", Encoding.ASCII.GetBytes("Location\0Paris")),
            data,
            Chunk("eXIf", [9, 9]),
            end,
            Encoding.ASCII.GetBytes("appended after the end"));

        var result = Strip(png);

        Assert.Equal(Concat([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], header, data, end), result);
    }

    /// <summary>
    /// A WebP loses its EXIF and XMP chunks, the extended header stops announcing them and the RIFF size follows.
    /// </summary>
    [Fact]
    public void WebP_LosesExifAndXmp()
    {
        var extended = new byte[10];
        extended[0] = 0x08 | 0x04 | 0x10;
        var picture = WebPChunk("VP8 ", [1, 2, 3, 4, 5]);
        var body = Concat(WebPChunk("VP8X", extended), picture, WebPChunk("EXIF", Encoding.ASCII.GetBytes("GPSDATA")), WebPChunk("XMP ", [7]));
        var webp = Concat(Encoding.ASCII.GetBytes("RIFF"), LittleEndian(body.Length + 4), Encoding.ASCII.GetBytes("WEBP"), body);

        var result = Strip(webp);

        Assert.DoesNotContain("GPSDATA", Encoding.ASCII.GetString(result), StringComparison.Ordinal);
        Assert.Equal(result.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(4)));
        Assert.Equal(0x10, result[20]);
        Assert.Equal(picture, result[^picture.Length..]);
    }

    /// <summary>
    /// Without stripping the bytes are the file; a file that is no picture has no layout.
    /// </summary>
    [Fact]
    public void Layout_KeepsTheFileWhenAskedAndRefusesOtherFiles()
    {
        var jpeg = Concat([0xFF, 0xD8], Segment(0xE1, Exif(orientation: 6)), Segment(0xDA, new byte[10]), [0x01, 0xFF, 0xD9]);
        Assert.Equal(jpeg, Read(jpeg, stripMetadata: false));

        using var text = new MemoryStream(Encoding.ASCII.GetBytes("just some text, not a picture"));
        Assert.Null(PictureLayout.Read(text, stripMetadata: true));
    }

    /// <summary>
    /// A JPEG whose segment claims more bytes than the file has is not offered at all.
    /// </summary>
    [Fact]
    public void Jpeg_WithBrokenSegmentIsRefused()
    {
        using var broken = new MemoryStream([0xFF, 0xD8, 0xFF, 0xE1, 0x40, 0x00, 0x45, 0x78, 0x69, 0x66, 0x00, 0x00]);

        Assert.Null(PictureLayout.Read(broken, stripMetadata: true));
    }

    /// <summary>
    /// The stream of a layout reads the same bytes from any position, as the chunk server reads it.
    /// </summary>
    [Fact]
    public void Stream_ReadsTheSameBytesFromAnyPosition()
    {
        var jpeg = Concat([0xFF, 0xD8], Segment(0xE1, Exif(orientation: 3)), Segment(0xDA, new byte[10]), Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray(), [0xFF, 0xD9]);
        var whole = Read(jpeg, stripMetadata: true);
        PictureLayout layout;
        using (var file = File.OpenRead(_path))
        {
            layout = PictureLayout.Read(file, stripMetadata: true)!;
        }

        using var stream = layout.Open(_path);
        foreach (var start in new[] { 0, 1, 37, 38, 50, 1000, whole.Length - 3 })
        {
            stream.Position = start;
            var part = new byte[Math.Min(700, whole.Length - start)];
            stream.ReadExactly(part);
            Assert.Equal(whole.AsSpan(start, part.Length).ToArray(), part);
        }
    }

    /// <summary>
    /// Strips the metadata of a picture and returns the bytes participants receive.
    /// </summary>
    /// <param name="picture">The picture file.</param>
    /// <returns>The bytes.</returns>
    private byte[] Strip(byte[] picture) => Read(picture, stripMetadata: true);

    /// <summary>
    /// Lays a picture out and reads the bytes participants receive.
    /// </summary>
    /// <param name="picture">The picture file.</param>
    /// <param name="stripMetadata">Whether the metadata is left out.</param>
    /// <returns>The bytes.</returns>
    private byte[] Read(byte[] picture, bool stripMetadata)
    {
        File.WriteAllBytes(_path, picture);
        PictureLayout layout;
        using (var file = File.OpenRead(_path))
        {
            layout = PictureLayout.Read(file, stripMetadata) ?? throw new InvalidDataException("No layout.");
        }

        using var stream = layout.Open(_path);
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        Assert.Equal(layout.Length, bytes.Length);
        return bytes;
    }

    /// <summary>
    /// Builds an EXIF block (big-endian TIFF) with an orientation and a camera name that stands for everything else.
    /// </summary>
    /// <param name="orientation">The orientation.</param>
    /// <returns>The data of an APP1 segment.</returns>
    private static byte[] Exif(ushort orientation)
    {
        var tiff = new byte[46];
        "MM\0*"u8.CopyTo(tiff);
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(8), 2);
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(10), 0x010F);
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(14), 8);
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(18), 38);
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(22), 0x0112);
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(24), 3);
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(30), orientation);
        "GPSDATA\0"u8.CopyTo(tiff.AsSpan(38));
        return Concat("Exif\0\0"u8.ToArray(), tiff);
    }

    /// <summary>
    /// Builds a JPEG segment.
    /// </summary>
    /// <param name="marker">The marker code.</param>
    /// <param name="data">The data.</param>
    /// <returns>The segment with its marker and length.</returns>
    private static byte[] Segment(byte marker, byte[] data)
    {
        var length = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)(data.Length + 2));
        return Concat([0xFF, marker], length, data);
    }

    /// <summary>
    /// Builds a PNG chunk (the checksum is not looked at and left empty).
    /// </summary>
    /// <param name="type">The chunk type.</param>
    /// <param name="data">The data.</param>
    /// <returns>The chunk.</returns>
    private static byte[] Chunk(string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        return Concat(length, Encoding.ASCII.GetBytes(type), data, new byte[4]);
    }

    /// <summary>
    /// Builds a WebP chunk, padded to an even size.
    /// </summary>
    /// <param name="type">The chunk type.</param>
    /// <param name="data">The data.</param>
    /// <returns>The chunk.</returns>
    private static byte[] WebPChunk(string type, byte[] data)
        => Concat(Encoding.ASCII.GetBytes(type), LittleEndian(data.Length), data, data.Length % 2 == 1 ? [0] : []);

    /// <summary>
    /// Writes a little-endian 32-bit number.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>Four bytes.</returns>
    private static byte[] LittleEndian(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }

    /// <summary>
    /// Joins byte arrays.
    /// </summary>
    /// <param name="parts">The parts.</param>
    /// <returns>The joined bytes.</returns>
    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
}
