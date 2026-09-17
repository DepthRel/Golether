using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Golether.Transports.Relay;

/// <summary>
/// STUN and TURN constants (RFC 5389, RFC 5766).
/// </summary>
public static class Stun
{
    /// <summary>
    /// The magic cookie.
    /// </summary>
    public const uint MagicCookie = 0x2112A442;

    /// <summary>
    /// The size of the message header.
    /// </summary>
    public const int HeaderSize = 20;

    /// <summary>
    /// Method: Binding.
    /// </summary>
    public const ushort Binding = 0x001;

    /// <summary>
    /// Method: Allocate.
    /// </summary>
    public const ushort Allocate = 0x003;

    /// <summary>
    /// Method: Refresh.
    /// </summary>
    public const ushort Refresh = 0x004;

    /// <summary>
    /// Method: Send (indication).
    /// </summary>
    public const ushort Send = 0x006;

    /// <summary>
    /// Method: Data (indication).
    /// </summary>
    public const ushort Data = 0x007;

    /// <summary>
    /// Method: CreatePermission.
    /// </summary>
    public const ushort CreatePermission = 0x008;

    /// <summary>
    /// Method: ChannelBind.
    /// </summary>
    public const ushort ChannelBind = 0x009;

    /// <summary>
    /// Attribute: MAPPED-ADDRESS.
    /// </summary>
    public const ushort AttrMappedAddress = 0x0001;

    /// <summary>
    /// Attribute: USERNAME.
    /// </summary>
    public const ushort AttrUsername = 0x0006;

    /// <summary>
    /// Attribute: MESSAGE-INTEGRITY.
    /// </summary>
    public const ushort AttrMessageIntegrity = 0x0008;

    /// <summary>
    /// Attribute: ERROR-CODE.
    /// </summary>
    public const ushort AttrErrorCode = 0x0009;

    /// <summary>
    /// Attribute: CHANNEL-NUMBER.
    /// </summary>
    public const ushort AttrChannelNumber = 0x000C;

    /// <summary>
    /// Attribute: LIFETIME.
    /// </summary>
    public const ushort AttrLifetime = 0x000D;

    /// <summary>
    /// Attribute: XOR-PEER-ADDRESS.
    /// </summary>
    public const ushort AttrXorPeerAddress = 0x0012;

    /// <summary>
    /// Attribute: DATA.
    /// </summary>
    public const ushort AttrData = 0x0013;

    /// <summary>
    /// Attribute: REALM.
    /// </summary>
    public const ushort AttrRealm = 0x0014;

    /// <summary>
    /// Attribute: NONCE.
    /// </summary>
    public const ushort AttrNonce = 0x0015;

    /// <summary>
    /// Attribute: XOR-RELAYED-ADDRESS.
    /// </summary>
    public const ushort AttrXorRelayedAddress = 0x0016;

    /// <summary>
    /// Attribute: REQUESTED-TRANSPORT.
    /// </summary>
    public const ushort AttrRequestedTransport = 0x0019;

    /// <summary>
    /// Attribute: XOR-MAPPED-ADDRESS.
    /// </summary>
    public const ushort AttrXorMappedAddress = 0x0020;

    /// <summary>
    /// Attribute: SOFTWARE.
    /// </summary>
    public const ushort AttrSoftware = 0x8022;

    /// <summary>
    /// Attribute: FINGERPRINT.
    /// </summary>
    public const ushort AttrFingerprint = 0x8028;

    /// <summary>
    /// Message class: request.
    /// </summary>
    public const ushort ClassRequest = 0x000;

    /// <summary>
    /// Message class: indication.
    /// </summary>
    public const ushort ClassIndication = 0x010;

    /// <summary>
    /// Message class: success response.
    /// </summary>
    public const ushort ClassSuccess = 0x100;

    /// <summary>
    /// Message class: error response.
    /// </summary>
    public const ushort ClassError = 0x110;

    /// <summary>
    /// The first channel number.
    /// </summary>
    public const ushort FirstChannel = 0x4000;

    /// <summary>
    /// The last channel number.
    /// </summary>
    public const ushort LastChannel = 0x7FFE;

    /// <summary>
    /// Derives the long-term credential key: MD5 of <c>username:realm:password</c>, as RFC 5389 requires.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <param name="realm">The realm.</param>
    /// <param name="password">The password.</param>
    /// <returns>The 16-byte key.</returns>
    public static byte[] LongTermKey(string username, string realm, string password)
#pragma warning disable CA5351 // MD5 is mandated by the TURN protocol for the key derivation.
        => MD5.HashData(System.Text.Encoding.UTF8.GetBytes($"{username}:{realm}:{password}"));
#pragma warning restore CA5351
}

/// <summary>
/// A STUN message: header and attributes in order.
/// </summary>
public sealed class StunMessage
{
    /// <summary>
    /// The largest message accepted.
    /// </summary>
    public const int MaxSize = 4096;

    /// <summary>
    /// Initializes a new instance of the <see cref="StunMessage"/> class.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <param name="messageClass">The class.</param>
    /// <param name="transactionId">The 12-byte transaction identifier.</param>
    public StunMessage(ushort method, ushort messageClass, ReadOnlySpan<byte> transactionId)
    {
        if (transactionId.Length != 12)
        {
            throw new ArgumentException("The transaction identifier has 12 bytes.", nameof(transactionId));
        }

        Method = method;
        Class = messageClass;
        TransactionId = transactionId.ToArray();
    }

    /// <summary>
    /// Gets the method.
    /// </summary>
    public ushort Method { get; }

    /// <summary>
    /// Gets the class.
    /// </summary>
    public ushort Class { get; }

    /// <summary>
    /// Gets the transaction identifier.
    /// </summary>
    public byte[] TransactionId { get; }

    /// <summary>
    /// Gets the attributes in order.
    /// </summary>
    public List<(ushort Type, byte[] Value)> Attributes { get; } = [];

    /// <summary>
    /// Gets the offset of MESSAGE-INTEGRITY in the parsed buffer, or -1.
    /// </summary>
    public int IntegrityOffset { get; private set; } = -1;

    /// <summary>
    /// Gets the raw bytes the message was parsed from.
    /// </summary>
    public byte[] Raw { get; private set; } = [];

    /// <summary>
    /// Combines a method and a class into the message type.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <param name="messageClass">The class bits (<see cref="Stun.ClassRequest"/> and so on).</param>
    /// <returns>The type.</returns>
    public static ushort TypeOf(ushort method, ushort messageClass)
    {
        // The method bits are interleaved with the two class bits at positions 4 and 8.
        var m = method & 0xFFF;
        var type = (m & 0x00F) | ((m & 0x070) << 1) | ((m & 0xF80) << 2);
        return (ushort)(type | messageClass);
    }

    /// <summary>
    /// Returns the total size of a STUN message from its header, or -1 when the header is not STUN.
    /// </summary>
    /// <param name="header">At least four bytes.</param>
    /// <returns>The size including the header.</returns>
    public static int MessageSize(ReadOnlySpan<byte> header)
        => (header[0] & 0xC0) == 0 ? Stun.HeaderSize + BinaryPrimitives.ReadUInt16BigEndian(header[2..]) : -1;

    /// <summary>
    /// Parses a message.
    /// </summary>
    /// <param name="data">The message bytes.</param>
    /// <returns>The message.</returns>
    /// <exception cref="InvalidDataException">The data is not a valid STUN message.</exception>
    public static StunMessage Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < Stun.HeaderSize || data.Length > MaxSize || (data[0] & 0xC0) != 0
            || BinaryPrimitives.ReadUInt32BigEndian(data[4..]) != Stun.MagicCookie)
        {
            throw new InvalidDataException("Not a STUN message.");
        }

        var type = BinaryPrimitives.ReadUInt16BigEndian(data);
        var length = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
        if (length % 4 != 0 || Stun.HeaderSize + length != data.Length)
        {
            throw new InvalidDataException("Invalid STUN length.");
        }

        var messageClass = (ushort)(type & 0x110);
        var raw = type & ~0x110;
        var method = (ushort)((raw & 0x00F) | ((raw & 0x0E0) >> 1) | ((raw & 0x3E00) >> 2));
        var message = new StunMessage(method, messageClass, data.Slice(8, 12)) { Raw = data.ToArray() };
        var offset = Stun.HeaderSize;
        while (offset < data.Length)
        {
            if (offset + 4 > data.Length)
            {
                throw new InvalidDataException("Truncated STUN attribute.");
            }

            var attributeType = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            var attributeLength = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..]);
            if (offset + 4 + attributeLength > data.Length)
            {
                throw new InvalidDataException("Truncated STUN attribute.");
            }

            if (message.IntegrityOffset < 0 || attributeType == Stun.AttrFingerprint)
            {
                if (attributeType == Stun.AttrMessageIntegrity)
                {
                    message.IntegrityOffset = offset;
                }

                message.Attributes.Add((attributeType, data.Slice(offset + 4, attributeLength).ToArray()));
            }

            offset += 4 + ((attributeLength + 3) & ~3);
        }

        return message;
    }

    /// <summary>
    /// Returns the value of the first attribute of a type.
    /// </summary>
    /// <param name="type">The attribute type.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public byte[]? Get(ushort type) => Attributes.FirstOrDefault(a => a.Type == type).Value;

    /// <summary>
    /// Returns all values of an attribute type.
    /// </summary>
    /// <param name="type">The attribute type.</param>
    /// <returns>The values.</returns>
    public IEnumerable<byte[]> GetAll(ushort type) => Attributes.Where(a => a.Type == type).Select(a => a.Value);

    /// <summary>
    /// Returns a UTF-8 attribute.
    /// </summary>
    /// <param name="type">The attribute type.</param>
    /// <returns>The text, or <see langword="null"/>.</returns>
    public string? GetText(ushort type) => Get(type) is { } value ? System.Text.Encoding.UTF8.GetString(value) : null;

    /// <summary>
    /// Adds an attribute.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="value">The value.</param>
    /// <returns>This message.</returns>
    public StunMessage Add(ushort type, byte[] value)
    {
        Attributes.Add((type, value));
        return this;
    }

    /// <summary>
    /// Adds a UTF-8 attribute.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="value">The text.</param>
    /// <returns>This message.</returns>
    public StunMessage AddText(ushort type, string value) => Add(type, System.Text.Encoding.UTF8.GetBytes(value));

    /// <summary>
    /// Adds a 32-bit attribute.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="value">The value.</param>
    /// <returns>This message.</returns>
    public StunMessage AddUInt32(ushort type, uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return Add(type, bytes);
    }

    /// <summary>
    /// Adds an XOR-encoded address attribute.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="endpoint">The address.</param>
    /// <returns>This message.</returns>
    public StunMessage AddXorAddress(ushort type, IPEndPoint endpoint) => Add(type, EncodeXorAddress(endpoint, TransactionId));

    /// <summary>
    /// Adds an error code.
    /// </summary>
    /// <param name="code">The code, for example 401.</param>
    /// <param name="reason">The reason phrase.</param>
    /// <returns>This message.</returns>
    public StunMessage AddError(int code, string reason)
    {
        var text = System.Text.Encoding.UTF8.GetBytes(reason);
        var value = new byte[4 + text.Length];
        value[2] = (byte)(code / 100);
        value[3] = (byte)(code % 100);
        text.CopyTo(value, 4);
        return Add(Stun.AttrErrorCode, value);
    }

    /// <summary>
    /// Reads an XOR-encoded address attribute.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>The address, or <see langword="null"/>.</returns>
    public IPEndPoint? GetXorAddress(ushort type) => Get(type) is { } value ? DecodeXorAddress(value, TransactionId) : null;

    /// <summary>
    /// Reads the error code of an error response.
    /// </summary>
    /// <returns>The code, or 0.</returns>
    public int GetErrorCode() => Get(Stun.AttrErrorCode) is { Length: >= 4 } value ? (value[2] * 100) + value[3] : 0;

    /// <summary>
    /// Checks MESSAGE-INTEGRITY with a key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when the message carries a valid integrity attribute.</returns>
    public bool VerifyIntegrity(byte[] key)
    {
        if (IntegrityOffset < 0 || Get(Stun.AttrMessageIntegrity) is not { Length: 20 } expected)
        {
            return false;
        }

        // The HMAC covers the message up to the attribute, with the length field counting up to its end.
        var covered = Raw.AsSpan(0, IntegrityOffset).ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(covered.AsSpan(2), (ushort)(IntegrityOffset + 24 - Stun.HeaderSize));
#pragma warning disable CA5350 // HMAC-SHA1 is mandated by STUN.
        var actual = HMACSHA1.HashData(key, covered);
#pragma warning restore CA5350
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>
    /// Serializes the message.
    /// </summary>
    /// <param name="integrityKey">The key for MESSAGE-INTEGRITY, or <see langword="null"/> to omit it.</param>
    /// <returns>The bytes, with FINGERPRINT.</returns>
    public byte[] Encode(byte[]? integrityKey = null)
    {
        var buffer = new MemoryStream();
        Span<byte> header = stackalloc byte[Stun.HeaderSize];
        BinaryPrimitives.WriteUInt16BigEndian(header, TypeOf(Method, Class));
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], Stun.MagicCookie);
        TransactionId.CopyTo(header[8..]);
        buffer.Write(header);
        foreach (var (type, value) in Attributes.Where(a => a.Type is not (Stun.AttrMessageIntegrity or Stun.AttrFingerprint)))
        {
            WriteAttribute(buffer, type, value);
        }

        if (integrityKey is not null)
        {
            SetLength(buffer, (int)buffer.Length + 24);
#pragma warning disable CA5350 // HMAC-SHA1 is mandated by STUN.
            var mac = HMACSHA1.HashData(integrityKey, buffer.ToArray());
#pragma warning restore CA5350
            WriteAttribute(buffer, Stun.AttrMessageIntegrity, mac);
        }

        SetLength(buffer, (int)buffer.Length + 8);
        var crc = Crc32.Compute(buffer.ToArray()) ^ 0x5354554E;
        var fingerprint = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(fingerprint, crc);
        WriteAttribute(buffer, Stun.AttrFingerprint, fingerprint);
        return buffer.ToArray();
    }

    /// <summary>
    /// Encodes an XOR address.
    /// </summary>
    /// <param name="endpoint">The address.</param>
    /// <param name="transactionId">The transaction identifier (for IPv6).</param>
    /// <returns>The attribute value.</returns>
    internal static byte[] EncodeXorAddress(IPEndPoint endpoint, byte[] transactionId)
    {
        var address = endpoint.Address.IsIPv4MappedToIPv6 ? endpoint.Address.MapToIPv4() : endpoint.Address;
        var bytes = address.GetAddressBytes();
        var value = new byte[4 + bytes.Length];
        value[1] = address.AddressFamily == AddressFamily.InterNetwork ? (byte)1 : (byte)2;
        BinaryPrimitives.WriteUInt16BigEndian(value.AsSpan(2), (ushort)(endpoint.Port ^ (Stun.MagicCookie >> 16)));
        var mask = Mask(transactionId);
        for (var i = 0; i < bytes.Length; i++)
        {
            value[4 + i] = (byte)(bytes[i] ^ mask[i]);
        }

        return value;
    }

    /// <summary>
    /// Decodes an XOR address.
    /// </summary>
    /// <param name="value">The attribute value.</param>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <returns>The address, or <see langword="null"/> when malformed.</returns>
    internal static IPEndPoint? DecodeXorAddress(byte[] value, byte[] transactionId)
    {
        var size = value.Length >= 4 ? value[1] switch { 1 => 4, 2 => 16, _ => -1 } : -1;
        if (size < 0 || value.Length != 4 + size)
        {
            return null;
        }

        var mask = Mask(transactionId);
        var bytes = new byte[size];
        for (var i = 0; i < size; i++)
        {
            bytes[i] = (byte)(value[4 + i] ^ mask[i]);
        }

        var port = BinaryPrimitives.ReadUInt16BigEndian(value.AsSpan(2)) ^ (int)(Stun.MagicCookie >> 16);
        return new IPEndPoint(new IPAddress(bytes), port);
    }

    /// <summary>
    /// Returns the XOR mask: the cookie followed by the transaction identifier.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <returns>The 16-byte mask.</returns>
    private static byte[] Mask(byte[] transactionId)
    {
        var mask = new byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(mask, Stun.MagicCookie);
        transactionId.CopyTo(mask, 4);
        return mask;
    }

    /// <summary>
    /// Writes an attribute with padding.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="type">The type.</param>
    /// <param name="value">The value.</param>
    private static void WriteAttribute(MemoryStream buffer, ushort type, byte[] value)
    {
        Span<byte> head = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(head, type);
        BinaryPrimitives.WriteUInt16BigEndian(head[2..], (ushort)value.Length);
        buffer.Write(head);
        buffer.Write(value);
        for (var i = value.Length; i % 4 != 0; i++)
        {
            buffer.WriteByte(0);
        }
    }

    /// <summary>
    /// Writes the length field so that the message ends at a size.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="totalSize">The message size including the header.</param>
    private static void SetLength(MemoryStream buffer, int totalSize)
    {
        var raw = buffer.GetBuffer();
        BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(2), (ushort)(totalSize - Stun.HeaderSize));
    }
}
