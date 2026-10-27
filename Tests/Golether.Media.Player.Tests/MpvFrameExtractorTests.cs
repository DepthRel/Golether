using System.Buffers.Binary;
using System.Text;
using Golether.Media.Player.Mpv;

namespace Golether.Media.Player.Tests;

/// <summary>
/// Tests of decoding one frame with the real libmpv prepared in <c>Native/&lt;runtime&gt;</c>: a picture participants
/// cannot decode becomes a JPEG, and a video gives a frame for its small picture. Skipped when the library is not there.
/// </summary>
public sealed class MpvFrameExtractorTests : IDisposable
{
    /// <summary>
    /// The folder of the test files.
    /// </summary>
    private readonly string _folder = Directory.CreateTempSubdirectory("golether-frames-").FullName;

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>
    /// A TIFF photograph becomes a JPEG of the same size; with a longest side given it becomes smaller, never larger.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Tiff_BecomesJpeg()
    {
        var extractor = Prepare();
        var token = TestContext.Current.CancellationToken;
        var tiff = Path.Combine(_folder, "фото 1.tif");
        await File.WriteAllBytesAsync(tiff, Tiff(96, 64), token);

        var full = Path.Combine(_folder, "full.jpg");
        Assert.True(await extractor.ExtractAsync(tiff, full, 0, 0, token));
        Assert.Equal((96, 64), JpegSize(await File.ReadAllBytesAsync(full, token)));

        var small = Path.Combine(_folder, "small.jpg");
        Assert.True(await extractor.ExtractAsync(tiff, small, 0, 48, token));
        Assert.Equal((48, 32), JpegSize(await File.ReadAllBytesAsync(small, token)));

        var large = Path.Combine(_folder, "large.jpg");
        Assert.True(await extractor.ExtractAsync(tiff, large, 0, 4000, token));
        Assert.Equal((96, 64), JpegSize(await File.ReadAllBytesAsync(large, token)));
        Assert.Equal(["full.jpg", "large.jpg", "small.jpg", "фото 1.tif"], Directory.EnumerateFileSystemEntries(_folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A frame inside a video is written; a file that is not a picture or a video gives nothing.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Video_GivesAFrame_AndGarbageGivesNothing()
    {
        var extractor = Prepare();
        var token = TestContext.Current.CancellationToken;
        var video = Path.Combine(_folder, "clip.y4m");
        await File.WriteAllBytesAsync(video, Y4m(64, 48, 25), token);

        var frame = Path.Combine(_folder, "frame.jpg");
        Assert.True(await extractor.ExtractAsync(video, frame, 0.1, 32, token));
        Assert.Equal((32, 24), JpegSize(await File.ReadAllBytesAsync(frame, token)));

        var garbage = Path.Combine(_folder, "broken.heic");
        await File.WriteAllBytesAsync(garbage, Encoding.ASCII.GetBytes(new string('x', 4000)), token);
        Assert.False(await extractor.ExtractAsync(garbage, Path.Combine(_folder, "nothing.jpg"), 0, 0, token));
        Assert.False(File.Exists(Path.Combine(_folder, "nothing.jpg")));
    }

    /// <summary>
    /// Makes the extractor over the libmpv of the repository, or skips the test.
    /// </summary>
    /// <returns>The extractor.</returns>
    private static MpvFrameExtractor Prepare()
    {
        var library = MpvInputTests.FindLibrary();
        Assert.SkipWhen(library is null, "libmpv is not prepared (run golether-components fetch).");
        MpvLibraryResolver.PreferredPath = library;
        var extractor = new MpvFrameExtractor();
        Assert.True(extractor.IsAvailable);
        return extractor;
    }

    /// <summary>
    /// Builds an uncompressed RGB TIFF of a colour gradient.
    /// </summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The file.</returns>
    private static byte[] Tiff(int width, int height)
    {
        const int ENTRIES = 9;
        var pixels = width * height * 3;
        var directory = 8 + pixels;
        var bits = directory + 2 + (ENTRIES * 12) + 4;
        var file = new byte[bits + 6];
        "II*\0"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), (uint)directory);
        for (var i = 0; i < pixels; i += 3)
        {
            var x = (i / 3) % width;
            var y = (i / 3) / width;
            file[8 + i] = (byte)(x * 255 / width);
            file[8 + i + 1] = (byte)(y * 255 / height);
            file[8 + i + 2] = 128;
        }

        var entries = new (ushort Tag, ushort Type, uint Value)[]
        {
            (256, 3, (uint)width), (257, 3, (uint)height), (258, 3, (uint)bits), (259, 3, 1), (262, 3, 2),
            (273, 4, 8), (277, 3, 3), (278, 3, (uint)height), (279, 4, (uint)pixels),
        };
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(directory), ENTRIES);
        for (var i = 0; i < ENTRIES; i++)
        {
            var at = directory + 2 + (i * 12);
            var (tag, type, value) = entries[i];
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at), tag);
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at + 2), type);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 4), tag == 258 ? 3u : 1u);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 8), value);
        }

        // Bits per sample: three values of 8, stored after the directory.
        for (var i = 0; i < 3; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(bits + (i * 2)), 8);
        }

        return file;
    }

    /// <summary>
    /// Builds an uncompressed YUV4MPEG2 video of grey frames growing lighter.
    /// </summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="frames">The number of frames, at 25 per second.</param>
    /// <returns>The file.</returns>
    private static byte[] Y4m(int width, int height, int frames)
    {
        using var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes($"YUV4MPEG2 W{width} H{height} F25:1 Ip A1:1 C420jpeg\n"));
        for (var i = 0; i < frames; i++)
        {
            stream.Write("FRAME\n"u8);
            stream.Write(Enumerable.Repeat((byte)(i * 8), width * height).ToArray());
            stream.Write(Enumerable.Repeat((byte)128, width * height / 2).ToArray());
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Reads the size of a JPEG from its frame header.
    /// </summary>
    /// <param name="jpeg">The JPEG.</param>
    /// <returns>The width and the height.</returns>
    private static (int Width, int Height) JpegSize(byte[] jpeg)
    {
        Assert.Equal([0xFF, 0xD8], jpeg[..2]);
        var position = 2;
        while (position + 9 < jpeg.Length)
        {
            var marker = jpeg[position + 1];
            if (marker is >= 0xC0 and <= 0xC3)
            {
                return (BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(position + 7)), BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(position + 5)));
            }

            position += 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(position + 2));
        }

        throw new InvalidDataException("No frame header.");
    }
}
