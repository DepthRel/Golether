using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Golether.Core.Identity;
using Golether.Security.Identity;

namespace Golether.Transports.Tls;

/// <summary>
/// Settings of the TLS transport.
/// </summary>
public sealed record TlsTransportOptions
{
    /// <summary>
    /// The default listening port.
    /// </summary>
    public const int DefaultPort = 47800;

    /// <summary>
    /// Gets the listening port; 0 selects a free port (default <see cref="DefaultPort"/>).
    /// </summary>
    public int Port { get; init; } = DefaultPort;

    /// <summary>
    /// Gets the time allowed for the TCP connection, TLS handshake and preamble (default 20 s; long-distance links with
    /// a round-trip time above one second need several round trips).
    /// </summary>
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Gets the maximum number of handshakes the listener runs at once (default 32).
    /// </summary>
    public int MaxPendingHandshakes { get; init; } = 32;

    /// <summary>
    /// Gets the socket buffer size for media streams (default 4 MiB).
    /// </summary>
    public int MediaSocketBufferSize { get; init; } = 4 * 1024 * 1024;

    /// <summary>
    /// Validates the options.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(Port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Port, 65535);
        if (HandshakeTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(HandshakeTimeout), "The handshake timeout must be positive.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(MaxPendingHandshakes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MediaSocketBufferSize, 64 * 1024);
    }
}

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
    public const byte Version = 1;

    /// <summary>
    /// The byte the listener sends when it accepts the preamble.
    /// </summary>
    public const byte Accepted = 0x01;

    /// <summary>
    /// The SNI name sent by connectors; certificates are pinned, so the name carries no meaning.
    /// </summary>
    public const string TargetHost = "golether";

    /// <summary>
    /// The TLS versions: TLS 1.3 where the system supports it, TLS 1.2 for Windows 10.
    /// </summary>
    public const SslProtocols Protocols = SslProtocols.Tls12 | SslProtocols.Tls13;

    /// <summary>
    /// Builds the preamble.
    /// </summary>
    /// <param name="purpose">The stream purpose.</param>
    /// <returns>The six preamble bytes.</returns>
    public static byte[] BuildPreamble(StreamPurpose purpose) => [.. Magic, Version, (byte)purpose];

    /// <summary>
    /// Parses a preamble.
    /// </summary>
    /// <param name="preamble">The six preamble bytes.</param>
    /// <param name="purpose">The stream purpose.</param>
    /// <returns><see langword="true"/> when the preamble is valid.</returns>
    public static bool TryParsePreamble(ReadOnlySpan<byte> preamble, out StreamPurpose purpose)
    {
        purpose = default;
        if (preamble.Length != 6 || !preamble[..4].SequenceEqual(Magic) || preamble[4] != Version)
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
    /// Applies socket options for the stream purpose.
    /// </summary>
    /// <param name="socket">The socket.</param>
    /// <param name="purpose">The stream purpose.</param>
    /// <param name="options">The transport options.</param>
    public static void ConfigureSocket(Socket socket, StreamPurpose purpose, TlsTransportOptions options)
    {
        socket.NoDelay = purpose == StreamPurpose.Control;
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        if (purpose == StreamPurpose.MediaData)
        {
            socket.ReceiveBufferSize = options.MediaSocketBufferSize;
            socket.SendBufferSize = options.MediaSocketBufferSize;
        }
    }
}
