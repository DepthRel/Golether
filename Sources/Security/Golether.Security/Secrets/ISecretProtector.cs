using System.Security.Cryptography;

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
