namespace Golether.Tunnels.AmneziaWG.Configuration;

/// <summary>
/// The <c>[Interface]</c> section of an AmneziaWG configuration.
/// </summary>
public sealed record AwgInterface
{
    /// <summary>
    /// Gets the base64 private key.
    /// </summary>
    public required string PrivateKey { get; init; }

    /// <summary>
    /// Gets the tunnel address with prefix, for example <c>10.77.41.1/24</c>.
    /// </summary>
    public required string Address { get; init; }

    /// <summary>
    /// Gets the UDP listening port, or <see langword="null"/> for a random port.
    /// </summary>
    public int? ListenPort { get; init; }

    /// <summary>
    /// Gets the MTU, or <see langword="null"/> for the default.
    /// </summary>
    public int? Mtu { get; init; }

    /// <summary>
    /// Gets the junk packet parameters of this side.
    /// </summary>
    public required JunkParameters Junk { get; init; }

    /// <summary>
    /// Gets the shared obfuscation parameters.
    /// </summary>
    public required SharedObfuscation Obfuscation { get; init; }

    /// <summary>
    /// Returns the section without exposing the private key in logs.
    /// </summary>
    /// <returns>The address and port.</returns>
    public override string ToString() => $"AwgInterface {{ Address = {Address}, ListenPort = {ListenPort} }}";
}
