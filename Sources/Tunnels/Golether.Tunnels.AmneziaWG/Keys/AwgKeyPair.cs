namespace Golether.Tunnels.AmneziaWG.Keys;

/// <summary>
/// A Curve25519 key pair of an AmneziaWG interface in the base64 form used by configuration files.
/// </summary>
/// <param name="PrivateKey">The base64 private key.</param>
/// <param name="PublicKey">The base64 public key.</param>
public sealed record AwgKeyPair(string PrivateKey, string PublicKey)
{
    /// <summary>
    /// Returns the key pair without exposing the private key in logs.
    /// </summary>
    /// <returns>The public key only.</returns>
    public override string ToString() => $"AwgKeyPair {{ PublicKey = {PublicKey} }}";
}
