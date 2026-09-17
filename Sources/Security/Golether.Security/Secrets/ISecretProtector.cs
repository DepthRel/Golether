using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Golether.Security.Secrets;

/// <summary>
/// Protects secrets (private keys, tunnel keys) before they are written to disk.
/// </summary>
public interface ISecretProtector
{
    /// <summary>
    /// Gets a short description for diagnostics, for example <c>DPAPI (current user)</c>.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Protects data.
    /// </summary>
    /// <param name="plain">The plain data.</param>
    /// <returns>The protected data.</returns>
    byte[] Protect(ReadOnlySpan<byte> plain);

    /// <summary>
    /// Restores protected data.
    /// </summary>
    /// <param name="protectedData">The protected data.</param>
    /// <returns>The plain data.</returns>
    /// <exception cref="CryptographicException">The data cannot be restored (another user, corrupted data).</exception>
    byte[] Unprotect(ReadOnlySpan<byte> protectedData);
}

/// <summary>
/// Windows DPAPI protection bound to the current user.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : ISecretProtector
{
    /// <summary>
    /// Additional entropy that separates Golether secrets from other DPAPI blobs of the user.
    /// </summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Golether.Secrets.v1");

    /// <inheritdoc />
    public string Description => "DPAPI (current user)";

    /// <inheritdoc />
    public byte[] Protect(ReadOnlySpan<byte> plain) => ProtectedData.Protect(plain.ToArray(), Entropy, DataProtectionScope.CurrentUser);

    /// <inheritdoc />
    public byte[] Unprotect(ReadOnlySpan<byte> protectedData) => ProtectedData.Unprotect(protectedData.ToArray(), Entropy, DataProtectionScope.CurrentUser);
}

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

/// <summary>
/// Chooses the secret protector of the current platform.
/// </summary>
public static class SecretProtectors
{
    /// <summary>
    /// Creates the default protector: DPAPI on Windows, file permissions elsewhere.
    /// </summary>
    /// <returns>The protector.</returns>
    public static ISecretProtector CreateDefault()
        => OperatingSystem.IsWindows() ? new DpapiSecretProtector() : new FilePermissionSecretProtector();

    /// <summary>
    /// Writes protected data to a file readable only by the current user.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="data">The protected data.</param>
    public static void WriteOwnerOnlyFile(string path, ReadOnlySpan<byte> data)
    {
        var temp = path + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var stream = new FileStream(temp, options))
        {
            stream.Write(data);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, path, overwrite: true);
    }
}
