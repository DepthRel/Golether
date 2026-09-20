using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Golether.Security.Secrets;

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
