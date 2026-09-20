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
