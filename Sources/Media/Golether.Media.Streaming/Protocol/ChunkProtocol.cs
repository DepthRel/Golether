using System.Buffers.Binary;
using Golether.Core.Media;

namespace Golether.Media.Streaming.Protocol;

/// <summary>
/// The status of a chunk response.
/// </summary>
public enum ChunkStatus : byte
{
    /// <summary>
    /// The chunk follows.
    /// </summary>
    Ok = 0,

    /// <summary>
    /// The index is outside the file.
    /// </summary>
    OutOfRange = 1,

    /// <summary>
    /// The host shares no media or another file than requested.
    /// </summary>
    WrongMedia = 2,

    /// <summary>
    /// The host failed to read the file.
    /// </summary>
    ReadError = 3,
}

/// <summary>
/// The binary protocol of media data streams.
/// </summary>
/// <remarks>
/// Request (17 bytes): operation (1), chunk index (8, big-endian), media key (8: the first bytes of the quick
/// identifier). Response header (45 bytes): status (1), chunk index (8), payload length (4), SHA-256 of the payload (32);
/// the payload follows. The media key makes the host reject requests for a file it no longer shares.
/// </remarks>
public static class ChunkProtocol
{
    /// <summary>
    /// The request operation that asks for a chunk.
    /// </summary>
    public const byte GetChunk = 1;

    /// <summary>
    /// The request operation that asks only for the SHA-256 of a chunk (the answer has no payload). Participants use
    /// it to check chunks received from other participants.
    /// </summary>
    public const byte GetHash = 2;

    /// <summary>
    /// The request size.
    /// </summary>
    public const int RequestSize = 17;

    /// <summary>
    /// The response header size.
    /// </summary>
    public const int ResponseHeaderSize = 45;

    /// <summary>
    /// Returns the media key of a descriptor.
    /// </summary>
    /// <param name="media">The descriptor.</param>
    /// <returns>The 8-byte key.</returns>
    public static ulong GetMediaKey(MediaDescriptor media)
    {
        ArgumentNullException.ThrowIfNull(media);
        return BinaryPrimitives.ReadUInt64BigEndian(Convert.FromHexString(media.QuickId.AsSpan(0, 16)));
    }

    /// <summary>
    /// Writes a request.
    /// </summary>
    /// <param name="destination">A buffer of at least <see cref="RequestSize"/> bytes.</param>
    /// <param name="index">The chunk index.</param>
    /// <param name="mediaKey">The media key.</param>
    /// <param name="operation"><see cref="GetChunk"/> or <see cref="GetHash"/>.</param>
    public static void WriteRequest(Span<byte> destination, long index, ulong mediaKey, byte operation = GetChunk)
    {
        destination[0] = operation;
        BinaryPrimitives.WriteInt64BigEndian(destination[1..], index);
        BinaryPrimitives.WriteUInt64BigEndian(destination[9..], mediaKey);
    }

    /// <summary>
    /// Reads a request.
    /// </summary>
    /// <param name="source">The request bytes.</param>
    /// <param name="operation">The operation.</param>
    /// <param name="index">The chunk index.</param>
    /// <param name="mediaKey">The media key.</param>
    /// <returns><see langword="false"/> when the operation is unknown.</returns>
    public static bool TryReadRequest(ReadOnlySpan<byte> source, out byte operation, out long index, out ulong mediaKey)
    {
        operation = source[0];
        index = BinaryPrimitives.ReadInt64BigEndian(source[1..]);
        mediaKey = BinaryPrimitives.ReadUInt64BigEndian(source[9..]);
        return operation is GetChunk or GetHash;
    }

    /// <summary>
    /// Writes a response header.
    /// </summary>
    /// <param name="destination">A buffer of at least <see cref="ResponseHeaderSize"/> bytes.</param>
    /// <param name="status">The status.</param>
    /// <param name="index">The chunk index.</param>
    /// <param name="length">The payload length.</param>
    /// <param name="hash">The SHA-256 of the payload (32 bytes, zeros without payload).</param>
    public static void WriteResponseHeader(Span<byte> destination, ChunkStatus status, long index, int length, ReadOnlySpan<byte> hash)
    {
        destination[0] = (byte)status;
        BinaryPrimitives.WriteInt64BigEndian(destination[1..], index);
        BinaryPrimitives.WriteInt32BigEndian(destination[9..], length);
        destination.Slice(13, 32).Clear();
        hash.CopyTo(destination[13..]);
    }
}
