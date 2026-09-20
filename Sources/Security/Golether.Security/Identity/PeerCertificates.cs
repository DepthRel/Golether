using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Golether.Core.Identity;

namespace Golether.Security.Identity;

/// <summary>
/// Helpers for peer certificates received from the network.
/// </summary>
public static class PeerCertificates
{
    /// <summary>
    /// The maximum accepted size of an embedded certificate.
    /// </summary>
    public const int MaxCertificateSize = 8 * 1024;

    /// <summary>
    /// Computes the identifier of a certificate from its public key.
    /// </summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns>The identifier.</returns>
    public static PeerId GetPeerId(X509Certificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (certificate is X509Certificate2 typed)
        {
            return PeerId.FromSubjectPublicKeyInfo(typed.PublicKey.ExportSubjectPublicKeyInfo());
        }

        using var loaded = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        return PeerId.FromSubjectPublicKeyInfo(loaded.PublicKey.ExportSubjectPublicKeyInfo());
    }

    /// <summary>
    /// Verifies a signature made with <see cref="DeviceIdentity.Sign"/> against a DER certificate and returns the
    /// signer identifier.
    /// </summary>
    /// <param name="certificateDer">The DER certificate of the signer.</param>
    /// <param name="data">The signed data.</param>
    /// <param name="signature">The signature.</param>
    /// <param name="signer">The identifier of the signer when the signature is valid.</param>
    /// <returns><see langword="true"/> when the certificate holds an ECDSA P-256 key and the signature is valid.</returns>
    public static bool TryVerify(ReadOnlySpan<byte> certificateDer, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, out PeerId signer)
    {
        signer = default;
        if (certificateDer.IsEmpty || certificateDer.Length > MaxCertificateSize || signature.IsEmpty)
        {
            return false;
        }

        try
        {
            using var certificate = X509CertificateLoader.LoadCertificate(certificateDer);
            using var key = certificate.GetECDsaPublicKey();
            if (key is null || key.KeySize != 256 || !key.VerifyData(data, signature, HashAlgorithmName.SHA256))
            {
                return false;
            }

            signer = GetPeerId(certificate);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
