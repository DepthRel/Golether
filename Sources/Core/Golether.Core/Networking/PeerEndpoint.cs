using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Golether.Core.Networking;

/// <summary>
/// A network address of a peer: a host name or IP address and a port.
/// </summary>
/// <param name="Host">The host name or IP address (IPv6 without brackets).</param>
/// <param name="Port">The port, 1–65535.</param>
public readonly record struct PeerEndpoint(string Host, int Port)
{
    /// <summary>
    /// The maximum length of a host name (RFC 1035).
    /// </summary>
    private const int MaxHostLength = 253;

    /// <summary>
    /// Parses <c>host:port</c>, <c>1.2.3.4:port</c> or <c>[v6]:port</c>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The endpoint.</returns>
    /// <exception cref="FormatException">The text is not a valid endpoint.</exception>
    public static PeerEndpoint Parse(string text)
        => TryParse(text, out var endpoint) ? endpoint : throw new FormatException($"'{text}' is not a valid host:port endpoint.");

    /// <summary>
    /// Tries to parse <c>host:port</c>, <c>1.2.3.4:port</c> or <c>[v6]:port</c>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="endpoint">The parsed endpoint.</param>
    /// <returns><see langword="true"/> when the text is valid.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, out PeerEndpoint endpoint)
    {
        endpoint = default;
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxHostLength + 8)
        {
            return false;
        }

        string host;
        string portText;
        if (text.StartsWith('['))
        {
            var close = text.IndexOf(']');
            if (close < 0 || close + 1 >= text.Length || text[close + 1] != ':')
            {
                return false;
            }

            host = text[1..close];
            portText = text[(close + 2)..];
            if (!IPAddress.TryParse(host, out var v6) || v6.AddressFamily != AddressFamily.InterNetworkV6)
            {
                return false;
            }
        }
        else
        {
            var colon = text.LastIndexOf(':');
            if (colon <= 0 || text.IndexOf(':') != colon)
            {
                return false;
            }

            host = text[..colon];
            portText = text[(colon + 1)..];
            if (!IsValidHost(host))
            {
                return false;
            }
        }

        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            return false;
        }

        endpoint = new PeerEndpoint(host, port);
        return true;
    }

    /// <summary>
    /// Formats the endpoint as <c>host:port</c> (IPv6 in brackets).
    /// </summary>
    /// <returns>The text form.</returns>
    public override string ToString()
        => IPAddress.TryParse(Host, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{Host}]:{Port.ToString(CultureInfo.InvariantCulture)}"
            : $"{Host}:{Port.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Checks a host name or IPv4 address.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <returns><see langword="true"/> when the host consists of valid DNS labels.</returns>
    private static bool IsValidHost(string host)
    {
        if (host.Length is 0 or > MaxHostLength)
        {
            return false;
        }

        foreach (var label in host.Split('.'))
        {
            if (label.Length is 0 or > 63 || label.StartsWith('-') || label.EndsWith('-')
                || !label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                return false;
            }
        }

        return true;
    }
}
