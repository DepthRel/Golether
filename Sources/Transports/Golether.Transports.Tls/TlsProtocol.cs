using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Golether.Core.Data.Enums;
using Golether.Core.Identity;
using Golether.Security.Identity;

namespace Golether.Transports.Tls;

/// <summary>
/// Shared TLS and preamble logic of the connector and the listener.
/// </summary>
internal static class TlsProtocol
{
    /// <summary>
    /// The preamble magic <c>GLTH</c>.
    /// </summary>
    public static ReadOnlySpan<byte> Magic => "GLTH"u8;

    /// <summary>
    /// The preamble version.
    /// </summary>
    public const byte VERSION = 1;

    /// <summary>
    /// The byte the listener sends when it accepts the preamble.
    /// </summary>
    public const byte ACCEPTED = 0x01;

    /// <summary>
    /// The SNI name sent by connectors; certificates are pinned, so the name carries no meaning.
    /// </summary>
    public const string TARGET_HOST = "golether";

    /// <summary>
    /// The TLS versions: TLS 1.3 where the system supports it, TLS 1.2 for Windows 10.
    /// </summary>
    public const SslProtocols PROTOCOLS = SslProtocols.Tls12 | SslProtocols.Tls13;

    /// <summary>
    /// Builds the preamble.
    /// </summary>
    /// <param name="purpose">The stream purpose.</param>
    /// <returns>The six preamble bytes.</returns>
    public static byte[] BuildPreamble(StreamPurpose purpose) => [.. Magic, VERSION, (byte)purpose];

    /// <summary>
    /// Parses a preamble.
    /// </summary>
    /// <param name="preamble">The six preamble bytes.</param>
    /// <param name="purpose">The stream purpose.</param>
    /// <returns><see langword="true"/> when the preamble is valid.</returns>
    public static bool TryParsePreamble(ReadOnlySpan<byte> preamble, out StreamPurpose purpose)
    {
        purpose = default;
        if (preamble.Length != 6 || !preamble[..4].SequenceEqual(Magic) || preamble[4] != VERSION)
        {
            return false;
        }

        purpose = (StreamPurpose)preamble[5];
        return Enum.IsDefined(purpose);
    }

    /// <summary>
    /// Creates the certificate context of the local identity.
    /// </summary>
    /// <param name="identity">The device identity.</param>
    /// <returns>The context; no chain is built for the self-signed certificate.</returns>
    public static SslStreamCertificateContext CreateContext(DeviceIdentity identity)
        => SslStreamCertificateContext.Create(identity.Certificate, additionalCertificates: null, offline: true);

    /// <summary>
    /// Extracts the identifier of the remote certificate.
    /// </summary>
    /// <param name="certificate">The remote certificate.</param>
    /// <param name="peer">The identifier.</param>
    /// <returns><see langword="true"/> when a certificate was presented.</returns>
    public static bool TryGetPeer(X509Certificate? certificate, out PeerId peer)
    {
        peer = default;
        if (certificate is null)
        {
            return false;
        }

        peer = PeerCertificates.GetPeerId(certificate);
        return true;
    }

    /// <summary>
    /// Reads an address of a connection; an IPv4 address seen through a dual socket is returned as plain IPv4.
    /// </summary>
    /// <param name="endPoint">The address of the socket.</param>
    /// <returns>The address, or <see langword="null"/> when it is not an IP address.</returns>
    public static System.Net.IPEndPoint? GetEndPoint(System.Net.EndPoint? endPoint)
        => endPoint is System.Net.IPEndPoint ip
            ? ip.Address.IsIPv4MappedToIPv6 ? new System.Net.IPEndPoint(ip.Address.MapToIPv4(), ip.Port) : ip
            : null;

    /// <summary>
    /// Applies socket options for the stream purpose.
    /// </summary>
    /// <param name="socket">The socket.</param>
    /// <param name="purpose">The stream purpose.</param>
    /// <param name="options">The transport options.</param>
    public static void ConfigureSocket(Socket socket, StreamPurpose purpose, TlsTransportOptions options)
    {
        // Voice packets inside a relay stream must not wait for more data either.
        socket.NoDelay = purpose is StreamPurpose.Control or StreamPurpose.Relay or StreamPurpose.RelayShared;
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        if (purpose == StreamPurpose.MediaData)
        {
            socket.ReceiveBufferSize = options.MediaSocketBufferSize;
            socket.SendBufferSize = options.MediaSocketBufferSize;
        }
    }
}
