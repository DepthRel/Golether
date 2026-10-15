using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Golether.Core.Networking;

namespace Golether.Security.Discovery;

/// <summary>
/// Hides a server address in a published file: AES-256-GCM with a key every copy of Golether carries.
/// </summary>
/// <remarks>
/// This is a mask, not a secret. The key is in the open source, so anybody who reads the code can open the address;
/// what the seal does is keep the address out of the sight of scanners that collect anything looking like
/// <c>host:port</c> from public pages. The tag of GCM also tells a damaged or foreign text from a real one. The
/// server itself must stay safe with its address known.
/// </remarks>
public static class EndpointSeal
{
    /// <summary>
    /// The beginning of a sealed address; names the format, so a later one can be told apart.
    /// </summary>
    public const string PREFIX = "g1.";

    /// <summary>
    /// The longest sealed text accepted.
    /// </summary>
    public const int MAX_LENGTH = 512;

    /// <summary>
    /// The size of the nonce of AES-GCM in bytes.
    /// </summary>
    private const int NONCE_BYTES = 12;

    /// <summary>
    /// The size of the tag of AES-GCM in bytes.
    /// </summary>
    private const int TAG_BYTES = 16;

    /// <summary>
    /// The key: SHA-256 of a fixed phrase. The utility that seals addresses derives the same one.
    /// </summary>
    private static readonly byte[] _key = SHA256.HashData(Encoding.UTF8.GetBytes("Golether server directory, seal 1"));

    /// <summary>
    /// Seals an address. Every call gives a different text: the nonce is random.
    /// </summary>
    /// <param name="endpoint">The address.</param>
    /// <returns>The sealed text: <see cref="PREFIX"/> and base64url of nonce, tag and ciphertext.</returns>
    public static string Seal(PeerEndpoint endpoint)
    {
        var plain = Encoding.UTF8.GetBytes(endpoint.ToString());
        var packed = new byte[NONCE_BYTES + TAG_BYTES + plain.Length];
        var nonce = packed.AsSpan(0, NONCE_BYTES);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TAG_BYTES);
        aes.Encrypt(nonce, plain, packed.AsSpan(NONCE_BYTES + TAG_BYTES), packed.AsSpan(NONCE_BYTES, TAG_BYTES));
        return PREFIX + Base64Url.EncodeToString(packed);
    }

    /// <summary>
    /// Opens a sealed address.
    /// </summary>
    /// <param name="text">The sealed text; it comes from the network and is not trusted.</param>
    /// <param name="endpoint">The address.</param>
    /// <returns><see langword="false"/> when the text is not a seal of this format, was changed or hides no address.</returns>
    public static bool TryOpen([NotNullWhen(true)] string? text, out PeerEndpoint endpoint)
    {
        endpoint = default;
        if (text is null || text.Length > MAX_LENGTH || !text.StartsWith(PREFIX, StringComparison.Ordinal))
        {
            return false;
        }

        var encoded = text.AsSpan(PREFIX.Length);
        if (!Base64Url.IsValid(encoded))
        {
            return false;
        }

        var packed = Base64Url.DecodeFromChars(encoded);
        if (packed.Length <= NONCE_BYTES + TAG_BYTES)
        {
            return false;
        }

        var plain = new byte[packed.Length - NONCE_BYTES - TAG_BYTES];
        try
        {
            using var aes = new AesGcm(_key, TAG_BYTES);
            aes.Decrypt(packed.AsSpan(0, NONCE_BYTES), packed.AsSpan(NONCE_BYTES + TAG_BYTES), packed.AsSpan(NONCE_BYTES, TAG_BYTES), plain);
        }
        catch (CryptographicException)
        {
            return false;
        }

        return PeerEndpoint.TryParse(Encoding.UTF8.GetString(plain), out endpoint);
    }
}
