using NSec.Cryptography;

namespace Golether.Tunnels.AmneziaWG.Keys;

/// <summary>
/// Generates and checks AmneziaWG (WireGuard) keys.
/// </summary>
public static class AwgKeys
{
    /// <summary>
    /// The key length in bytes.
    /// </summary>
    public const int KeyLength = 32;

    /// <summary>
    /// Generates a new key pair.
    /// </summary>
    /// <returns>The key pair.</returns>
    public static AwgKeyPair Generate()
    {
        var parameters = new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
        using var key = Key.Create(KeyAgreementAlgorithm.X25519, parameters);
        return new AwgKeyPair(
            Convert.ToBase64String(key.Export(KeyBlobFormat.RawPrivateKey)),
            Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey)));
    }

    /// <summary>
    /// Computes the public key of a private key.
    /// </summary>
    /// <param name="privateKey">The base64 private key.</param>
    /// <returns>The base64 public key.</returns>
    /// <exception cref="FormatException">The private key is invalid.</exception>
    public static string GetPublicKey(string privateKey)
    {
        if (!IsValidKey(privateKey))
        {
            throw new FormatException("The private key must be 32 bytes in base64.");
        }

        var parameters = new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
        using var key = Key.Import(KeyAgreementAlgorithm.X25519, Convert.FromBase64String(privateKey), KeyBlobFormat.RawPrivateKey, parameters);
        return Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
    }

    /// <summary>
    /// Checks the format of a base64 key.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> for 32 bytes in canonical base64.</returns>
    public static bool IsValidKey(string? value)
    {
        if (value is null || value.Length != 44)
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[KeyLength];
        return Convert.TryFromBase64String(value, buffer, out var written) && written == KeyLength;
    }
}
