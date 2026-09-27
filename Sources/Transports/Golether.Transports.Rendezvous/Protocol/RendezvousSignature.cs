using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Golether.Core.Identity;

namespace Golether.Transports.Rendezvous.Protocol;

/// <summary>
/// Proves that a host owns the device key it presents when it takes a room.
/// </summary>
/// <remarks>
/// The host signs the nonce of the connection together with the room, so a recorded registration cannot be replayed
/// and a signature for one room does not open another. The room then belongs to that key: another device cannot
/// take it over, while the host itself can come back after losing the connection.
/// </remarks>
public static class RendezvousSignature
{
    /// <summary>
    /// The largest accepted certificate: 8 KiB (a device certificate is well under 1 KiB).
    /// </summary>
    public const int MaxCertificateSize = 8 * 1024;

    /// <summary>
    /// The largest accepted signature (an ECDSA P-256 signature takes 64–72 bytes).
    /// </summary>
    public const int MaxSignatureSize = 256;

    /// <summary>
    /// The context string that keeps these signatures apart from the other signatures of the device key.
    /// </summary>
    private const string Context = "golether-rendezvous-v1";

    /// <summary>
    /// Builds the bytes the host signs.
    /// </summary>
    /// <param name="nonce">The nonce of the connection.</param>
    /// <param name="room">The room.</param>
    /// <returns>The bytes.</returns>
    public static byte[] BuildPayload(string nonce, string room)
    {
        ArgumentNullException.ThrowIfNull(nonce);
        ArgumentNullException.ThrowIfNull(room);
        return Encoding.UTF8.GetBytes($"{Context}\n{nonce}\n{room}");
    }

    /// <summary>
    /// Checks a signature made with an ECDSA P-256 device key.
    /// </summary>
    /// <param name="certificateDer">The certificate of the device (DER).</param>
    /// <param name="payload">The signed bytes.</param>
    /// <param name="signature">The signature.</param>
    /// <param name="owner">The identifier of the device key.</param>
    /// <returns><see langword="true"/> when the signature is valid.</returns>
    public static bool TryVerify(ReadOnlySpan<byte> certificateDer, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> signature, out PeerId owner)
    {
        owner = default;
        if (certificateDer.IsEmpty || certificateDer.Length > MaxCertificateSize || signature.IsEmpty || signature.Length > MaxSignatureSize)
        {
            return false;
        }

        try
        {
            using var certificate = X509CertificateLoader.LoadCertificate(certificateDer);
            using var key = certificate.GetECDsaPublicKey();
            if (key is null || key.KeySize != 256 || !key.VerifyData(payload, signature, HashAlgorithmName.SHA256))
            {
                return false;
            }

            owner = PeerId.FromSubjectPublicKeyInfo(certificate.PublicKey.ExportSubjectPublicKeyInfo());
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// Decodes a base64 field of a message.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="maxBytes">The largest accepted size.</param>
    /// <param name="bytes">The bytes.</param>
    /// <returns><see langword="true"/> when the text is valid base64 within the limit.</returns>
    public static bool TryDecode(string? text, int maxBytes, [NotNullWhen(true)] out byte[]? bytes)
    {
        bytes = null;
        if (string.IsNullOrEmpty(text) || text.Length > ((maxBytes + 2) / 3 * 4))
        {
            return false;
        }

        var buffer = new byte[maxBytes];
        if (!Convert.TryFromBase64String(text, buffer, out var written) || written == 0)
        {
            return false;
        }

        bytes = buffer[..written];
        return true;
    }
}
