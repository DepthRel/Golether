using System.Buffers.Binary;
using System.Text;

namespace Golether.Media.Streaming.Gallery;

/// <summary>
/// The bytes of a picture as participants receive them: pieces of the file, with the metadata (the place it was taken
/// at, the camera, the author, comments) left out when the host asks so. Only the headers are read to build it, so a
/// folder of hundreds of photographs is prepared in moments; the picture itself is never decoded or rewritten.
/// </summary>
/// <remarks>
/// JPEG keeps its frame, tables, scans and colour profile; the EXIF block is replaced by one that holds the
/// orientation only, so an upright photo stays upright, and whatever follows the end of the picture (further pictures
/// some cameras append, a short video of a "live" photo) is left out with its own metadata. PNG loses its text and
/// EXIF chunks and anything after its end, WebP its EXIF and XMP chunks, GIF its comments and application blocks
/// other than the loop and the colour profile.
/// </remarks>
public sealed class PictureLayout
{
    /// <summary>
    /// The most JPEG segments read before the picture data: a real photograph has a dozen or two.
    /// </summary>
    private const int MAX_HEADER_SEGMENTS = 256;

    /// <summary>
    /// The PNG chunks that carry text or camera data and are left out.
    /// </summary>
    private static readonly string[] _pngMetadataChunks = ["tEXt", "zTXt", "iTXt", "eXIf", "tIME"];

    /// <summary>
    /// The pieces in order.
    /// </summary>
    private readonly IReadOnlyList<Piece> _pieces;

    /// <summary>
    /// Initializes a new instance of the <see cref="PictureLayout"/> class.
    /// </summary>
    /// <param name="pieces">The pieces in order.</param>
    private PictureLayout(IReadOnlyList<Piece> pieces)
    {
        _pieces = pieces;
        Length = pieces.Sum(p => p.Length);
    }

    /// <summary>
    /// Gets the number of bytes participants receive.
    /// </summary>
    public long Length { get; }

    /// <summary>
    /// Builds the layout of a picture.
    /// </summary>
    /// <param name="file">The picture file, readable and seekable.</param>
    /// <param name="stripMetadata">Whether the metadata is left out.</param>
    /// <returns>The layout, or <see langword="null"/> when the file is not a picture of a known format.</returns>
    public static PictureLayout? Read(Stream file, bool stripMetadata)
    {
        ArgumentNullException.ThrowIfNull(file);
        var length = file.Length;
        Span<byte> signature = stackalloc byte[12];
        file.Position = 0;
        if (file.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false) < signature.Length)
        {
            return null;
        }

        var whole = new PictureLayout([Piece.Range(0, length)]);
        try
        {
            if (signature[0] == 0xFF && signature[1] == 0xD8 && signature[2] == 0xFF)
            {
                return stripMetadata ? ReadJpeg(file, length) : whole;
            }

            if (signature[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            {
                return stripMetadata ? ReadPng(file, length) : whole;
            }

            if (signature[..4].SequenceEqual("RIFF"u8) && signature[8..12].SequenceEqual("WEBP"u8))
            {
                return stripMetadata ? ReadWebP(file, length) : whole;
            }

            if (signature[..3].SequenceEqual("GIF"u8))
            {
                return stripMetadata ? ReadGif(file, length) : whole;
            }

            // BMP carries nothing about the place or the camera.
            return signature[..2].SequenceEqual("BM"u8) ? whole : null;
        }
        catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException)
        {
            // A broken header: the file is not offered at all rather than sent with its metadata.
            return null;
        }
    }

    /// <summary>
    /// Opens the bytes participants receive.
    /// </summary>
    /// <param name="path">The picture file.</param>
    /// <returns>A readable, seekable stream of <see cref="Length"/> bytes.</returns>
    public Stream Open(string path) => new PieceStream(Files.MediaFiles.OpenRead(path), _pieces, Length);

    /// <summary>
    /// Lays out a JPEG: the segments before the scan are kept or left out one by one, the rest is kept.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="length">The file length.</param>
    /// <returns>The layout.</returns>
    private static PictureLayout ReadJpeg(Stream file, long length)
    {
        var kept = new List<Piece>();
        ushort orientation = 1;
        var position = 2L;
        for (var segment = 0; segment < MAX_HEADER_SEGMENTS; segment++)
        {
            var marker = ReadMarker(file, ref position);
            if (marker is >= 0xD0 and <= 0xD7 or 0x01)
            {
                kept.Add(Piece.Range(position - 2, 2));
                continue;
            }

            if (marker == 0xD9)
            {
                // An empty picture.
                kept.Add(Piece.Range(position - 2, 2));
                return Build(kept, orientation);
            }

            if (marker == 0xDA)
            {
                ReadScans(file, length, position - 2, kept);
                return Build(kept, orientation);
            }

            var size = ReadUInt16(file, position);
            if (size < 2 || position + size > length)
            {
                throw new InvalidDataException("A JPEG segment runs past the end of the file.");
            }

            var start = position - 2;
            if (IsJpegMetadata(file, marker, position + 2, size - 2, ref orientation))
            {
                position += size;
                continue;
            }

            kept.Add(Piece.Range(start, size + 2));
            position += size;
        }

        throw new InvalidDataException("The JPEG has too many header segments.");
    }

    /// <summary>
    /// Keeps the scans of a JPEG up to the end of its picture. Segments between scans that are metadata are left out,
    /// and so is everything after the end marker. A file cut short is kept to its end.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="length">The file length.</param>
    /// <param name="start">The position of the first start-of-scan marker.</param>
    /// <param name="kept">The kept pieces; the scans are added.</param>
    private static void ReadScans(Stream file, long length, long start, List<Piece> kept)
    {
        var buffer = new byte[64 * 1024];
        var rangeStart = start;
        var position = start + 2 + ReadUInt16(file, start + 2);
        while (FindMarker(file, length, buffer, ref position) is var marker and >= 0)
        {
            if (marker == 0xD9)
            {
                kept.Add(Piece.Range(rangeStart, position + 2 - rangeStart));
                return;
            }

            var size = ReadUInt16(file, position + 2);
            if (size < 2 || position + 2 + size > length)
            {
                throw new InvalidDataException("A JPEG segment runs past the end of the file.");
            }

            ushort ignored = 1;
            if (IsJpegMetadata(file, (byte)marker, position + 4, size - 2, ref ignored))
            {
                kept.Add(Piece.Range(rangeStart, position - rangeStart));
                rangeStart = position + 2 + size;
            }

            position += 2 + size;
        }

        kept.Add(Piece.Range(rangeStart, length - rangeStart));
    }

    /// <summary>
    /// Finds the next marker of a JPEG at or after a position, passing over coded picture data: there a 0xFF byte is
    /// followed by a zero, by a restart code or by another 0xFF, never by a marker code.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="length">The file length.</param>
    /// <param name="buffer">A buffer to read into.</param>
    /// <param name="position">Where to look from, past the segment handled last; moved to the 0xFF byte of the
    /// marker.</param>
    /// <returns>The marker code, or -1 when the file ends first.</returns>
    private static int FindMarker(Stream file, long length, byte[] buffer, ref long position)
    {
        var from = position;
        while (from < length - 1)
        {
            file.Position = from;
            var count = file.Read(buffer, 0, (int)Math.Min(buffer.Length, length - from));
            if (count < 2)
            {
                break;
            }

            var offset = 0;
            while (true)
            {
                var found = buffer.AsSpan(offset, count - offset).IndexOf((byte)0xFF);
                if (found < 0)
                {
                    from += count;
                    break;
                }

                var at = offset + found;
                if (at + 1 >= count)
                {
                    // The byte after it is in the next read; at least two bytes were read, so this moves on.
                    from += at;
                    break;
                }

                var code = buffer[at + 1];
                if (code == 0x00 || code is >= 0xD0 and <= 0xD7 || code == 0xFF)
                {
                    offset = code == 0xFF ? at + 1 : at + 2;
                    continue;
                }

                position = from + at;
                return code;
            }
        }

        return -1;
    }

    /// <summary>
    /// Lays out a GIF: comments and application blocks are left out, except the loop of an animation and the colour
    /// profile; nothing after the end of the picture is kept.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="length">The file length.</param>
    /// <returns>The layout.</returns>
    private static PictureLayout ReadGif(Stream file, long length)
    {
        var pieces = new List<Piece>();
        var reader = new ForwardReader(file, 10);
        var rangeStart = 0L;
        var screen = reader.ReadByte();
        reader.Skip(2);
        if ((screen & 0x80) != 0)
        {
            reader.Skip(3 << ((screen & 7) + 1));
        }

        try
        {
            while (true)
            {
                var blockStart = reader.Position;
                switch (reader.ReadByte())
                {
                    case 0x3B:
                        pieces.Add(Piece.Range(rangeStart, reader.Position - rangeStart));
                        return new PictureLayout(pieces);
                    case 0x2C:
                        reader.Skip(8);
                        var image = reader.ReadByte();
                        if ((image & 0x80) != 0)
                        {
                            reader.Skip(3 << ((image & 7) + 1));
                        }

                        reader.Skip(1);
                        reader.SkipSubBlocks();
                        break;
                    case 0x21:
                        if (IsGifMetadata(reader))
                        {
                            pieces.Add(Piece.Range(rangeStart, blockStart - rangeStart));
                            rangeStart = reader.Position;
                        }

                        break;
                    default:
                        throw new InvalidDataException("An unknown GIF block.");
                }

                if (reader.Position > length)
                {
                    throw new InvalidDataException("A GIF block runs past the end of the file.");
                }
            }
        }
        catch (EndOfStreamException)
        {
            // A file cut short: what is there is kept.
            pieces.Add(Piece.Range(rangeStart, length - rangeStart));
            return new PictureLayout(Merge(pieces.Where(p => p.Length > 0).ToList()));
        }
    }

    /// <summary>
    /// Reads a GIF extension and decides whether it is metadata.
    /// </summary>
    /// <param name="reader">The reader, past the extension introducer; moved past the extension.</param>
    /// <returns><see langword="true"/> when the extension is left out.</returns>
    private static bool IsGifMetadata(ForwardReader reader)
    {
        var label = reader.ReadByte();
        if (label != 0xFF)
        {
            // A comment is left out; frame timing and plain text are the picture itself.
            reader.SkipSubBlocks();
            return label == 0xFE;
        }

        var size = reader.ReadByte();
        var application = reader.Read(size);
        reader.SkipSubBlocks();
        return !(application.AsSpan().StartsWith("NETSCAPE2.0"u8) ||
            application.AsSpan().StartsWith("ANIMEXTS1.0"u8) ||
            application.AsSpan().StartsWith("ICCRGBG1012"u8));
    }

    /// <summary>
    /// Puts the start of the image and the orientation block in front of the kept segments.
    /// </summary>
    /// <param name="kept">The kept segments.</param>
    /// <param name="orientation">The EXIF orientation of the picture.</param>
    /// <returns>The layout.</returns>
    private static PictureLayout Build(List<Piece> kept, ushort orientation)
    {
        var pieces = new List<Piece> { Piece.Inline([0xFF, 0xD8]) };
        if (orientation is > 1 and <= 8)
        {
            pieces.Add(Piece.Inline(OrientationSegment(orientation)));
        }

        pieces.AddRange(kept);
        return new PictureLayout(pieces);
    }

    /// <summary>
    /// Reads the next JPEG marker, skipping fill bytes.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="position">The position of the marker; moved past it.</param>
    /// <returns>The marker code.</returns>
    private static byte ReadMarker(Stream file, ref long position)
    {
        file.Position = position;
        if (file.ReadByte() != 0xFF)
        {
            throw new InvalidDataException("A JPEG marker was expected.");
        }

        int code;
        do
        {
            code = file.ReadByte();
            position++;
        }
        while (code == 0xFF);

        position++;
        return code < 0 ? throw new EndOfStreamException() : (byte)code;
    }

    /// <summary>
    /// Decides whether a JPEG segment is metadata, and reads the orientation out of an EXIF block.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="marker">The marker of the segment.</param>
    /// <param name="dataStart">Where the data of the segment starts.</param>
    /// <param name="dataLength">The length of the data.</param>
    /// <param name="orientation">The orientation found so far; updated from an EXIF block.</param>
    /// <returns><see langword="true"/> when the segment is left out.</returns>
    private static bool IsJpegMetadata(Stream file, byte marker, long dataStart, int dataLength, ref ushort orientation)
    {
        if (marker == 0xFE)
        {
            // A comment.
            return true;
        }

        if (marker is < 0xE1 or > 0xEF)
        {
            // Frames, tables, restart intervals and the JFIF block (APP0) are the picture itself.
            return false;
        }

        if (marker == 0xE2)
        {
            // The colour profile (ICC) keeps the colours right; other APP2 blocks (FlashPix, MPF) are left out.
            var head = ReadBytes(file, dataStart, Math.Min(dataLength, 12));
            return !head.AsSpan().StartsWith("ICC_PROFILE\0"u8);
        }

        if (marker == 0xEE)
        {
            // The Adobe block says how the colours of the picture are coded; without it some files turn purple.
            var head = ReadBytes(file, dataStart, Math.Min(dataLength, 5));
            return !head.AsSpan().StartsWith("Adobe"u8);
        }

        if (marker == 0xE1 && dataLength <= 64 * 1024)
        {
            var exif = ReadBytes(file, dataStart, dataLength);
            if (exif.AsSpan().StartsWith("Exif\0\0"u8) && ReadOrientation(exif.AsSpan(6)) is { } found)
            {
                orientation = found;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads the orientation tag from the first directory of an EXIF (TIFF) block.
    /// </summary>
    /// <param name="tiff">The TIFF data.</param>
    /// <returns>The orientation, or <see langword="null"/> when it is not there or the block is broken.</returns>
    private static ushort? ReadOrientation(ReadOnlySpan<byte> tiff)
    {
        const ushort ORIENTATION_TAG = 0x0112;
        if (tiff.Length < 8)
        {
            return null;
        }

        var little = tiff[0] == (byte)'I' && tiff[1] == (byte)'I';
        if (!little && !(tiff[0] == (byte)'M' && tiff[1] == (byte)'M'))
        {
            return null;
        }

        var directory = ReadUInt32(tiff[4..], little);
        if (directory > (uint)tiff.Length - 2)
        {
            return null;
        }

        var count = ReadUInt16(tiff[(int)directory..], little);
        for (var i = 0; i < count; i++)
        {
            var entry = (int)directory + 2 + (i * 12);
            if (entry + 12 > tiff.Length)
            {
                return null;
            }

            if (ReadUInt16(tiff[entry..], little) == ORIENTATION_TAG)
            {
                var value = ReadUInt16(tiff[(entry + 8)..], little);
                return value is >= 1 and <= 8 ? value : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds an EXIF block that holds the orientation and nothing else.
    /// </summary>
    /// <param name="orientation">The orientation, 2 to 8.</param>
    /// <returns>The APP1 segment with its marker.</returns>
    private static byte[] OrientationSegment(ushort orientation)
    {
        // Marker (2), length (2), "Exif\0\0" (6), TIFF header (8), one entry (2 + 12), no next directory (4).
        var segment = new byte[36];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), 34);
        "Exif\0\0"u8.CopyTo(segment.AsSpan(4));
        "MM\0*"u8.CopyTo(segment.AsSpan(10));
        BinaryPrimitives.WriteUInt32BigEndian(segment.AsSpan(14), 8);
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(18), 1);
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(20), 0x0112);
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(22), 3);
        BinaryPrimitives.WriteUInt32BigEndian(segment.AsSpan(24), 1);
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(28), orientation);
        return segment;
    }

    /// <summary>
    /// Lays out a PNG: every chunk is kept except those with text or camera data.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="length">The file length.</param>
    /// <returns>The layout.</returns>
    private static PictureLayout ReadPng(Stream file, long length)
    {
        var pieces = new List<Piece> { Piece.Range(0, 8) };
        var position = 8L;
        while (position < length)
        {
            if (position + 12 > length)
            {
                throw new InvalidDataException("A PNG chunk runs past the end of the file.");
            }

            var header = ReadBytes(file, position, 8);
            var size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var total = 12L + size;
            if (position + total > length)
            {
                throw new InvalidDataException("A PNG chunk runs past the end of the file.");
            }

            var type = Encoding.ASCII.GetString(header, 4, 4);
            if (!_pngMetadataChunks.Contains(type, StringComparer.Ordinal))
            {
                pieces.Add(Piece.Range(position, total));
            }

            position += total;
            if (type == "IEND")
            {
                break;
            }
        }

        return new PictureLayout(Merge(pieces));
    }

    /// <summary>
    /// Lays out a WebP: the EXIF and XMP chunks are left out, and the extended header no longer announces them.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="length">The file length.</param>
    /// <returns>The layout.</returns>
    private static PictureLayout ReadWebP(Stream file, long length)
    {
        const byte XMP_FLAG = 0x04;
        const byte EXIF_FLAG = 0x08;
        var chunks = new List<Piece>();
        var position = 12L;
        var riffEnd = Math.Min(length, 8L + ReadUInt32Le(file, 4));
        while (position + 8 <= riffEnd)
        {
            var header = ReadBytes(file, position, 8);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
            var total = 8L + size + (size & 1);
            if (position + total > length)
            {
                throw new InvalidDataException("A WebP chunk runs past the end of the file.");
            }

            var type = Encoding.ASCII.GetString(header, 0, 4);
            if (type == "VP8X" && size >= 10)
            {
                var extended = ReadBytes(file, position, (int)total);
                extended[8] &= unchecked((byte)~(XMP_FLAG | EXIF_FLAG));
                chunks.Add(Piece.Inline(extended));
            }
            else if (type is not ("EXIF" or "XMP "))
            {
                chunks.Add(Piece.Range(position, total));
            }

            position += total;
        }

        var body = chunks.Sum(c => c.Length);
        var riff = new byte[12];
        "RIFF"u8.CopyTo(riff);
        BinaryPrimitives.WriteUInt32LittleEndian(riff.AsSpan(4), (uint)(body + 4));
        "WEBP"u8.CopyTo(riff.AsSpan(8));
        return new PictureLayout([Piece.Inline(riff), .. Merge(chunks)]);
    }

    /// <summary>
    /// Joins neighbouring file ranges into one, so a PNG of thousands of data chunks is not read in thousands of
    /// pieces.
    /// </summary>
    /// <param name="pieces">The pieces in order.</param>
    /// <returns>The joined pieces.</returns>
    private static List<Piece> Merge(List<Piece> pieces)
    {
        var merged = new List<Piece>(pieces.Count);
        foreach (var piece in pieces)
        {
            if (merged.Count > 0 && merged[^1] is { Bytes: null } last && piece.Bytes is null && last.Offset + last.Length == piece.Offset)
            {
                merged[^1] = Piece.Range(last.Offset, last.Length + piece.Length);
            }
            else
            {
                merged.Add(piece);
            }
        }

        return merged;
    }

    /// <summary>
    /// Reads bytes at a position.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="position">The position.</param>
    /// <param name="count">The number of bytes.</param>
    /// <returns>The bytes.</returns>
    private static byte[] ReadBytes(Stream file, long position, int count)
    {
        var bytes = new byte[count];
        file.Position = position;
        file.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>
    /// Reads a big-endian 16-bit number at a position.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="position">The position.</param>
    /// <returns>The number.</returns>
    private static ushort ReadUInt16(Stream file, long position) => BinaryPrimitives.ReadUInt16BigEndian(ReadBytes(file, position, 2));

    /// <summary>
    /// Reads a little-endian 32-bit number at a position.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="position">The position.</param>
    /// <returns>The number.</returns>
    private static uint ReadUInt32Le(Stream file, long position) => BinaryPrimitives.ReadUInt32LittleEndian(ReadBytes(file, position, 4));

    /// <summary>
    /// Reads a 16-bit number of a TIFF block.
    /// </summary>
    /// <param name="data">The bytes, starting with the number.</param>
    /// <param name="little">Whether the block is little-endian.</param>
    /// <returns>The number.</returns>
    private static ushort ReadUInt16(ReadOnlySpan<byte> data, bool little)
        => little ? BinaryPrimitives.ReadUInt16LittleEndian(data) : BinaryPrimitives.ReadUInt16BigEndian(data);

    /// <summary>
    /// Reads a 32-bit number of a TIFF block.
    /// </summary>
    /// <param name="data">The bytes, starting with the number.</param>
    /// <param name="little">Whether the block is little-endian.</param>
    /// <returns>The number.</returns>
    private static uint ReadUInt32(ReadOnlySpan<byte> data, bool little)
        => little ? BinaryPrimitives.ReadUInt32LittleEndian(data) : BinaryPrimitives.ReadUInt32BigEndian(data);

    /// <summary>
    /// A piece of the bytes participants receive: a range of the file or bytes made here.
    /// </summary>
    /// <param name="Offset">The start of the range in the file.</param>
    /// <param name="Length">The number of bytes.</param>
    /// <param name="Bytes">The bytes made here, or <see langword="null"/> for a range of the file.</param>
    private sealed record Piece(long Offset, long Length, byte[]? Bytes)
    {
        /// <summary>
        /// Makes a piece of the file.
        /// </summary>
        /// <param name="offset">The start in the file.</param>
        /// <param name="length">The number of bytes.</param>
        /// <returns>The piece.</returns>
        public static Piece Range(long offset, long length) => new(offset, length, null);

        /// <summary>
        /// Makes a piece of bytes made here.
        /// </summary>
        /// <param name="bytes">The bytes.</param>
        /// <returns>The piece.</returns>
        public static Piece Inline(byte[] bytes) => new(0, bytes.Length, bytes);
    }

    /// <summary>
    /// Reads a file forward through a buffer, for formats of many small blocks (GIF).
    /// </summary>
    private sealed class ForwardReader
    {
        /// <summary>
        /// The file.
        /// </summary>
        private readonly Stream _file;

        /// <summary>
        /// The bytes read ahead.
        /// </summary>
        private readonly byte[] _buffer = new byte[64 * 1024];

        /// <summary>
        /// The next byte of the buffer.
        /// </summary>
        private int _offset;

        /// <summary>
        /// The number of bytes in the buffer.
        /// </summary>
        private int _count;

        /// <summary>
        /// Initializes a new instance of the <see cref="ForwardReader"/> class.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="start">Where reading starts.</param>
        public ForwardReader(Stream file, long start)
        {
            _file = file;
            _file.Position = start;
            Position = start;
        }

        /// <summary>
        /// Gets the position of the next byte in the file.
        /// </summary>
        public long Position { get; private set; }

        /// <summary>
        /// Reads a byte.
        /// </summary>
        /// <returns>The byte.</returns>
        /// <exception cref="EndOfStreamException">The file ended.</exception>
        public byte ReadByte()
        {
            if (_offset == _count)
            {
                Fill();
            }

            Position++;
            return _buffer[_offset++];
        }

        /// <summary>
        /// Reads bytes.
        /// </summary>
        /// <param name="count">The number of bytes.</param>
        /// <returns>The bytes.</returns>
        public byte[] Read(int count)
        {
            var bytes = new byte[count];
            for (var i = 0; i < count; i++)
            {
                bytes[i] = ReadByte();
            }

            return bytes;
        }

        /// <summary>
        /// Passes over bytes.
        /// </summary>
        /// <param name="count">The number of bytes.</param>
        public void Skip(int count)
        {
            while (count > 0)
            {
                if (_offset == _count)
                {
                    Fill();
                }

                var step = Math.Min(count, _count - _offset);
                _offset += step;
                Position += step;
                count -= step;
            }
        }

        /// <summary>
        /// Passes over a chain of GIF data sub-blocks, up to and including the empty one that ends it.
        /// </summary>
        public void SkipSubBlocks()
        {
            for (var size = ReadByte(); size != 0; size = ReadByte())
            {
                Skip(size);
            }
        }

        /// <summary>
        /// Reads the next bytes of the file into the buffer.
        /// </summary>
        /// <exception cref="EndOfStreamException">The file ended.</exception>
        private void Fill()
        {
            _count = _file.Read(_buffer);
            _offset = 0;
            if (_count == 0)
            {
                throw new EndOfStreamException();
            }
        }
    }

    /// <summary>
    /// A read-only, seekable stream over the pieces of a picture.
    /// </summary>
    private sealed class PieceStream : Stream
    {
        /// <summary>
        /// The picture file.
        /// </summary>
        private readonly Stream _file;

        /// <summary>
        /// The pieces in order.
        /// </summary>
        private readonly IReadOnlyList<Piece> _pieces;

        /// <summary>
        /// The total length.
        /// </summary>
        private readonly long _length;

        /// <summary>
        /// The current position.
        /// </summary>
        private long _position;

        /// <summary>
        /// Initializes a new instance of the <see cref="PieceStream"/> class.
        /// </summary>
        /// <param name="file">The picture file; the stream owns it.</param>
        /// <param name="pieces">The pieces in order.</param>
        /// <param name="length">The total length.</param>
        public PieceStream(Stream file, IReadOnlyList<Piece> pieces, long length)
        {
            _file = file;
            _pieces = pieces;
            _length = length;
        }

        /// <inheritdoc />
        public override bool CanRead => true;

        /// <inheritdoc />
        public override bool CanSeek => true;

        /// <inheritdoc />
        public override bool CanWrite => false;

        /// <inheritdoc />
        public override long Length => _length;

        /// <inheritdoc />
        public override long Position
        {
            get => _position;
            set => _position = Math.Clamp(value, 0, _length);
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        /// <inheritdoc />
        public override int Read(Span<byte> buffer)
        {
            var written = 0;
            var start = 0L;
            foreach (var piece in _pieces)
            {
                if (written == buffer.Length || _position >= _length)
                {
                    break;
                }

                var end = start + piece.Length;
                if (_position < end)
                {
                    var inside = _position - start;
                    var count = (int)Math.Min(buffer.Length - written, piece.Length - inside);
                    var target = buffer.Slice(written, count);
                    if (piece.Bytes is { } bytes)
                    {
                        bytes.AsSpan((int)inside, count).CopyTo(target);
                    }
                    else
                    {
                        _file.Position = piece.Offset + inside;
                        _file.ReadExactly(target);
                    }

                    written += count;
                    _position += count;
                }

                start = end;
            }

            return written;
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                _ => _length + offset,
            };
            return _position;
        }

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _file.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
