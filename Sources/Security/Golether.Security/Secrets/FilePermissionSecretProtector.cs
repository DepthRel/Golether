using System.Security.Cryptography;

namespace Golether.Security.Secrets;

/// <summary>
/// Stores secrets as they are, relying on owner-only file permissions (mode 600 in a mode 700 directory).
/// </summary>
/// <remarks>
/// Used on Linux and macOS until the Secret Service (libsecret) and Keychain backends are implemented.
/// A versioned header lets a later backend recognise and migrate such blobs.
/// </remarks>
public sealed class FilePermissionSecretProtector : ISecretProtector
{
    /// <summary>
    /// The header that marks unencrypted blobs.
    /// </summary>
    private static readonly byte[] Header = "GLTPLAIN1"u8.ToArray();

    /// <inheritdoc />
    public string Description => "file permissions (owner only)";

    /// <inheritdoc />
    public byte[] Protect(ReadOnlySpan<byte> plain)
    {
        var result = new byte[Header.Length + plain.Length];
        Header.CopyTo(result, 0);
        plain.CopyTo(result.AsSpan(Header.Length));
        return result;
    }

    /// <inheritdoc />
    public byte[] Unprotect(ReadOnlySpan<byte> protectedData)
    {
        if (!protectedData.StartsWith(Header))
        {
            throw new CryptographicException("The secret was not written by this protector.");
        }

        return protectedData[Header.Length..].ToArray();
    }
}
