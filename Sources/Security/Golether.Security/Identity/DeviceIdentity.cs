using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Golether.Core.Identity;

namespace Golether.Security.Identity;

/// <summary>
/// The key pair of this device: an ECDSA P-256 key with a self-signed certificate used for mutual TLS and for signing
/// invitations and tunnel packages.
/// </summary>
/// <remarks>
/// The certificate is not trusted through a certificate authority: peers pin the <see cref="PeerId"/> derived from its
/// public key.
/// </remarks>
public sealed class DeviceIdentity : IDisposable
{
    /// <summary>
    /// OID of the TLS server authentication extended key usage.
    /// </summary>
    private const string ServerAuthOid = "1.3.6.1.5.5.7.3.1";

    /// <summary>
    /// OID of the TLS client authentication extended key usage.
    /// </summary>
    private const string ClientAuthOid = "1.3.6.1.5.5.7.3.2";

    /// <summary>
    /// Initializes a new instance of the <see cref="DeviceIdentity"/> class.
    /// </summary>
    /// <param name="certificate">The certificate with an ECDSA P-256 private key; ownership is transferred.</param>
    /// <exception cref="ArgumentException">The certificate has no ECDSA P-256 private key.</exception>
    public DeviceIdentity(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (!certificate.HasPrivateKey)
        {
            throw new ArgumentException("The device certificate must contain a private key.", nameof(certificate));
        }

        using var publicKey = certificate.GetECDsaPublicKey()
            ?? throw new ArgumentException("The device certificate must use an ECDSA key.", nameof(certificate));
        if (publicKey.KeySize != 256)
        {
            throw new ArgumentException("The device key must be ECDSA P-256.", nameof(certificate));
        }

        Certificate = certificate;
        PeerId = PeerCertificates.GetPeerId(certificate);
    }

    /// <summary>
    /// Gets the certificate with the private key.
    /// </summary>
    public X509Certificate2 Certificate { get; }

    /// <summary>
    /// Gets the identifier derived from the public key.
    /// </summary>
    public PeerId PeerId { get; }

    /// <summary>
    /// Gets the DER encoding of the certificate without the private key, for embedding into signed packages.
    /// </summary>
    public byte[] PublicCertificateDer => Certificate.RawData;

    /// <summary>
    /// Creates a new identity.
    /// </summary>
    /// <param name="timeProvider">The time provider for the validity period.</param>
    /// <returns>The identity.</returns>
    public static DeviceIdentity CreateNew(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Golether device", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(ServerAuthOid), new Oid(ClientAuthOid)], false));

        var now = timeProvider.GetUtcNow();
        using var created = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(20));

        // SChannel on Windows cannot use an ephemeral key from CreateSelfSigned for TLS: re-import through PKCS#12.
        return FromPkcs12(created.Export(X509ContentType.Pkcs12));
    }

    /// <summary>
    /// Loads an identity from PKCS#12 data without a password.
    /// </summary>
    /// <param name="pkcs12">The PKCS#12 data.</param>
    /// <returns>The identity.</returns>
    /// <exception cref="CryptographicException">The data is not a valid PKCS#12 blob.</exception>
    public static DeviceIdentity FromPkcs12(byte[] pkcs12)
    {
        ArgumentNullException.ThrowIfNull(pkcs12);
        var certificate = X509CertificateLoader.LoadPkcs12(pkcs12, null, X509KeyStorageFlags.Exportable);
        return new DeviceIdentity(certificate);
    }

    /// <summary>
    /// Exports the identity as PKCS#12 data without a password; protect the result before storing it.
    /// </summary>
    /// <returns>The PKCS#12 data.</returns>
    public byte[] ExportPkcs12() => Certificate.Export(X509ContentType.Pkcs12);

    /// <summary>
    /// Signs data with the device key (ECDSA P-256, SHA-256, IEEE P1363 format).
    /// </summary>
    /// <param name="data">The data.</param>
    /// <returns>The signature.</returns>
    public byte[] Sign(ReadOnlySpan<byte> data)
    {
        using var key = Certificate.GetECDsaPrivateKey()
            ?? throw new InvalidOperationException("The device certificate has no ECDSA private key.");
        return key.SignData(data, HashAlgorithmName.SHA256);
    }

    /// <summary>
    /// Releases the certificate and its key.
    /// </summary>
    public void Dispose() => Certificate.Dispose();
}

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
