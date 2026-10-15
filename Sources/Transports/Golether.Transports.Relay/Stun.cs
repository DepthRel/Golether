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
    public const uint MAGIC_COOKIE = 0x2112A442;

    /// <summary>
    /// The size of the message header.
    /// </summary>
    public const int HEADER_SIZE = 20;

    /// <summary>
    /// Method: Binding.
    /// </summary>
    public const ushort BINDING = 0x001;

    /// <summary>
    /// Method: Allocate.
    /// </summary>
    public const ushort ALLOCATE = 0x003;

    /// <summary>
    /// Method: Refresh.
    /// </summary>
    public const ushort REFRESH = 0x004;

    /// <summary>
    /// Method: Send (indication).
    /// </summary>
    public const ushort SEND = 0x006;

    /// <summary>
    /// Method: Data (indication).
    /// </summary>
    public const ushort DATA = 0x007;

    /// <summary>
    /// Method: CreatePermission.
    /// </summary>
    public const ushort CREATE_PERMISSION = 0x008;

    /// <summary>
    /// Method: ChannelBind.
    /// </summary>
    public const ushort CHANNEL_BIND = 0x009;

    /// <summary>
    /// Attribute: MAPPED-ADDRESS.
    /// </summary>
    public const ushort ATTR_MAPPED_ADDRESS = 0x0001;

    /// <summary>
    /// Attribute: USERNAME.
    /// </summary>
    public const ushort ATTR_USERNAME = 0x0006;

    /// <summary>
    /// Attribute: MESSAGE-INTEGRITY.
    /// </summary>
    public const ushort ATTR_MESSAGE_INTEGRITY = 0x0008;

    /// <summary>
    /// Attribute: ERROR-CODE.
    /// </summary>
    public const ushort ATTR_ERROR_CODE = 0x0009;

    /// <summary>
    /// Attribute: CHANNEL-NUMBER.
    /// </summary>
    public const ushort ATTR_CHANNEL_NUMBER = 0x000C;

    /// <summary>
    /// Attribute: LIFETIME.
    /// </summary>
    public const ushort ATTR_LIFETIME = 0x000D;

    /// <summary>
    /// Attribute: XOR-PEER-ADDRESS.
    /// </summary>
    public const ushort ATTR_XOR_PEER_ADDRESS = 0x0012;

    /// <summary>
    /// Attribute: DATA.
    /// </summary>
    public const ushort ATTR_DATA = 0x0013;

    /// <summary>
    /// Attribute: REALM.
    /// </summary>
    public const ushort ATTR_REALM = 0x0014;

    /// <summary>
    /// Attribute: NONCE.
    /// </summary>
    public const ushort ATTR_NONCE = 0x0015;

    /// <summary>
    /// Attribute: XOR-RELAYED-ADDRESS.
    /// </summary>
    public const ushort ATTR_XOR_RELAYED_ADDRESS = 0x0016;

    /// <summary>
    /// Attribute: REQUESTED-TRANSPORT.
    /// </summary>
    public const ushort ATTR_REQUESTED_TRANSPORT = 0x0019;

    /// <summary>
    /// Attribute: XOR-MAPPED-ADDRESS.
    /// </summary>
    public const ushort ATTR_XOR_MAPPED_ADDRESS = 0x0020;

    /// <summary>
    /// Attribute: SOFTWARE.
    /// </summary>
    public const ushort ATTR_SOFTWARE = 0x8022;

    /// <summary>
    /// Attribute: FINGERPRINT.
    /// </summary>
    public const ushort ATTR_FINGERPRINT = 0x8028;

    /// <summary>
    /// Message class: request.
    /// </summary>
    public const ushort CLASS_REQUEST = 0x000;

    /// <summary>
    /// Message class: indication.
    /// </summary>
    public const ushort CLASS_INDICATION = 0x010;

    /// <summary>
    /// Message class: success response.
    /// </summary>
    public const ushort CLASS_SUCCESS = 0x100;

    /// <summary>
    /// Message class: error response.
    /// </summary>
    public const ushort CLASS_ERROR = 0x110;

    /// <summary>
    /// The first channel number.
    /// </summary>
    public const ushort FIRST_CHANNEL = 0x4000;

    /// <summary>
    /// The last channel number.
    /// </summary>
    public const ushort LAST_CHANNEL = 0x7FFE;

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
