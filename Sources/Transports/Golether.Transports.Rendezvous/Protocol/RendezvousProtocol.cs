using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Golether.Transports.Rendezvous.Protocol;

/// <summary>
/// Constants and validation shared by the rendezvous server and the devices.
/// </summary>
public static class RendezvousProtocol
{
    /// <summary>
    /// The protocol version implemented by this build.
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// The default port of the server: TCP for meetings, UDP for STUN.
    /// </summary>
    public const int DefaultPort = 47900;

    /// <summary>
    /// The largest frame either side accepts: 16 KiB (a certificate and a signature fit easily).
    /// </summary>
    public const int MaxFrameSize = 16 * 1024;

    /// <summary>
    /// The largest number of local addresses a device may report.
    /// </summary>
    public const int MaxLocals = 8;

    /// <summary>
    /// The number of random bytes in a nonce and in a meeting identifier.
    /// </summary>
    public const int RandomBytes = 16;

    /// <summary>
    /// Creates a random nonce or meeting identifier (base64url).
    /// </summary>
    /// <returns>The value.</returns>
    public static string CreateRandomId() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RandomBytes));

    /// <summary>
    /// Checks the format of a nonce or meeting identifier.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when it has the format of <see cref="CreateRandomId"/>.</returns>
    public static bool IsValidRandomId([NotNullWhen(true)] string? value)
        => value is { Length: 22 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>
    /// Formats an address for a message: <c>1.2.3.4:5</c>.
    /// </summary>
    /// <param name="endpoint">The address.</param>
    /// <returns>The text.</returns>
    public static string Format(IPEndPoint endpoint) => Normalize(endpoint).ToString();

    /// <summary>
    /// Turns an IPv4 address seen through a dual-mode socket (<c>::ffff:1.2.3.4</c>) into a plain IPv4 address.
    /// </summary>
    /// <param name="endpoint">The address.</param>
    /// <returns>The address, IPv4 where possible.</returns>
    public static IPEndPoint Normalize(IPEndPoint endpoint)
        => endpoint.Address.IsIPv4MappedToIPv6 ? new IPEndPoint(endpoint.Address.MapToIPv4(), endpoint.Port) : endpoint;

    /// <summary>
    /// Parses an address from a message. Only IPv4 addresses with a port are accepted: meetings run over IPv4, and a
    /// host name here would make the device ask DNS on behalf of somebody else.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="endpoint">The address.</param>
    /// <returns><see langword="true"/> when the text is a usable IPv4 address.</returns>
    public static bool TryParseEndpoint([NotNullWhen(true)] string? text, [NotNullWhen(true)] out IPEndPoint? endpoint)
    {
        endpoint = null;
        if (text is null || text.Length > 21 || !IPEndPoint.TryParse(text, out var parsed)
            || parsed.AddressFamily != AddressFamily.InterNetwork || parsed.Port is < 1 or > 65535
            || parsed.Address.Equals(IPAddress.Any) || parsed.Address.Equals(IPAddress.Broadcast))
        {
            return false;
        }

        endpoint = parsed;
        return true;
    }

    /// <summary>
    /// Parses the local addresses of a message, dropping the unusable ones.
    /// </summary>
    /// <param name="locals">The texts, or <see langword="null"/>.</param>
    /// <returns>At most <see cref="MaxLocals"/> addresses.</returns>
    public static IReadOnlyList<IPEndPoint> ParseLocals(IEnumerable<string>? locals)
        => locals is null
            ? []
            : [.. locals.Take(MaxLocals).Select(l => TryParseEndpoint(l, out var e) ? e : null).OfType<IPEndPoint>().Distinct()];
}
