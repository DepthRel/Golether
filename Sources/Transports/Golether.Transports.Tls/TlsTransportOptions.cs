namespace Golether.Transports.Tls;

/// <summary>
/// Settings of the TLS transport.
/// </summary>
public sealed record TlsTransportOptions
{
    /// <summary>
    /// The default listening port.
    /// </summary>
    public const int DEFAULT_PORT = 47800;

    /// <summary>
    /// Gets the listening port; 0 selects a free port (default <see cref="DEFAULT_PORT"/>).
    /// </summary>
    public int Port { get; init; } = DEFAULT_PORT;

    /// <summary>
    /// Gets the time allowed for the TCP connection, TLS handshake and preamble (default 20 s; long-distance links with
    /// a round-trip time above one second need several round trips).
    /// </summary>
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Gets the maximum number of handshakes the listener runs at once (default 128).
    /// </summary>
    public int MaxPendingHandshakes { get; init; } = 128;

    /// <summary>
    /// Gets the maximum number of handshakes the listener runs at once for one remote address (default 16): a few
    /// silent connections from one place must not take every handshake and keep the participants out. A whole
    /// company behind one router opens a few streams each, one after another, which stays well below it.
    /// </summary>
    public int MaxPendingHandshakesPerAddress { get; init; } = 16;

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
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxPendingHandshakesPerAddress, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MediaSocketBufferSize, 64 * 1024);
    }
}
