using System.Security.Cryptography;
using Golether.Core.Networking;
using Golether.Security.Identity;
using Golether.Security.Verification;
using Golether.Tunnels.AmneziaWG.Configuration;
using Golether.Tunnels.AmneziaWG.Keys;

namespace Golether.Tunnels.AmneziaWG.Packages;

/// <summary>
/// Negotiates an AmneziaWG tunnel: the host creates an offer, the participant accepts it, and the host completes it.
/// </summary>
/// <remarks>
/// The preshared key is agreed with ephemeral P-256 key pairs (ECDH) on both sides, so it never appears in either
/// package. The verification code is derived from both device identifiers and the offer identifier, so the two
/// people can compare it by voice.
/// </remarks>
public sealed class TunnelNegotiator
{
    /// <summary>
    /// The default offer lifetime.
    /// </summary>
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(1);

    /// <summary>
    /// The local device.
    /// </summary>
    private readonly DeviceIdentity _identity;

    /// <summary>
    /// The time provider.
    /// </summary>
    private readonly TimeProvider _time;

    /// <summary>
    /// Initializes a new instance of the <see cref="TunnelNegotiator"/> class.
    /// </summary>
    /// <param name="identity">The local device.</param>
    /// <param name="time">The time provider.</param>
    public TunnelNegotiator(DeviceIdentity identity, TimeProvider time)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// Creates an offer on the host side and returns the package together with the secrets to keep.
    /// </summary>
    /// <param name="hostInterface">The host tunnel interface.</param>
    /// <param name="hostName">The host name to show the participant.</param>
    /// <param name="endpoints">The candidate UDP endpoints of the host.</param>
    /// <param name="usedAddresses">The participant addresses already assigned.</param>
    /// <param name="ttl">The offer lifetime, <see langword="null"/> for the default.</param>
    /// <returns>The offer.</returns>
    /// <exception cref="InvalidOperationException">The subnet has no free addresses.</exception>
    public HostOffer CreateOffer(
        HostTunnelInterface hostInterface,
        string hostName,
        IReadOnlyList<PeerEndpoint> endpoints,
        IReadOnlyList<string> usedAddresses,
        TimeSpan? ttl = null)
    {
        ArgumentNullException.ThrowIfNull(hostInterface);
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(usedAddresses);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostName);
        hostInterface.Obfuscation.Validate();

        using var ephemeral = NewEphemeralKey();
        var offerId = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var assignedAddress = hostInterface.NextParticipantAddress(usedAddresses);
        var expiresAt = _time.GetUtcNow().Add(ttl ?? DefaultTtl);

        var body = new TunnelOfferBody
        {
            OfferId = offerId,
            HostName = hostName,
            HostCertificate = Convert.ToBase64String(_identity.PublicCertificateDer),
            HostPublicKey = hostInterface.Keys.PublicKey,
            HostEndpoints = endpoints.Select(e => e.ToString()).ToArray(),
            HostAddress = hostInterface.HostAddress,
            AssignedAddress = assignedAddress,
            S1 = hostInterface.Obfuscation.S1,
            S2 = hostInterface.Obfuscation.S2,
            Headers = [hostInterface.Obfuscation.H1, hostInterface.Obfuscation.H2, hostInterface.Obfuscation.H3, hostInterface.Obfuscation.H4],
            EphemeralKey = EphemeralPublicKey(ephemeral),
            ExpiresAt = expiresAt.ToUnixTimeSeconds(),
        };

        var packageText = TunnelPackageCodec.Encode(TunnelPackageCodec.OfferKind, body, _identity);
        return new HostOffer(packageText, new PendingOfferSecrets(offerId, EphemeralPrivateKey(ephemeral), assignedAddress, expiresAt));
    }

    /// <summary>
    /// Accepts an offer on the participant side: verifies it, builds the local tunnel configuration and signs the
    /// answer.
    /// </summary>
    /// <param name="offerText">The offer package.</param>
    /// <param name="participantName">The participant name to show the host.</param>
    /// <param name="publicEndpoints">The candidate UDP endpoints of the participant (may be empty).</param>
    /// <param name="listenPort">The UDP port of the participant interface.</param>
    /// <returns>The acceptance.</returns>
    /// <exception cref="FormatException">The offer is invalid, expired, own, or has no usable endpoint.</exception>
    public ParticipantAcceptance AcceptOffer(
        string offerText,
        string participantName,
        IReadOnlyList<PeerEndpoint> publicEndpoints,
        int listenPort)
    {
        ArgumentNullException.ThrowIfNull(publicEndpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(participantName);
        ArgumentOutOfRangeException.ThrowIfLessThan(listenPort, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(listenPort, 65535);

        var offer = TunnelPackageCodec.Decode<TunnelOfferBody>(TunnelPackageCodec.OfferKind, offerText, b => b.HostCertificate);
        if (offer.Signer == _identity.PeerId)
        {
            throw new FormatException("Это предложение принадлежит вашему устройству.");
        }

        if (offer.Body.ExpiresAt <= _time.GetUtcNow().ToUnixTimeSeconds())
        {
            throw new FormatException("Срок действия предложения истёк. Попросите ведущего создать новое.");
        }

        var hostEndpoint = FirstEndpoint(offer.Body.HostEndpoints);
        if (hostEndpoint is null)
        {
            throw new FormatException("В предложении нет адреса, по которому можно связаться с ведущим.");
        }

        using var ephemeral = NewEphemeralKey();
        var preshared = PresharedKey(ephemeral, offer.Body.EphemeralKey);
        var keys = AwgKeys.Generate();

        var hostPeer = new AwgPeer
        {
            Comment = offer.Body.HostName,
            PublicKey = offer.Body.HostPublicKey,
            PresharedKey = preshared,
            AllowedIps = [$"{offer.Body.HostAddress}/32"],
            Endpoint = hostEndpoint,
            PersistentKeepalive = HostTunnelInterface.KeepaliveSeconds,
        };

        var obfuscation = new SharedObfuscation(
            offer.Body.S1,
            offer.Body.S2,
            offer.Body.Headers[0],
            offer.Body.Headers[1],
            offer.Body.Headers[2],
            offer.Body.Headers[3]);
        var configuration = new AwgConfiguration(
            new AwgInterface
            {
                PrivateKey = keys.PrivateKey,
                Address = $"{offer.Body.AssignedAddress}/32",
                ListenPort = listenPort,
                Junk = JunkParameters.Generate(),
                Obfuscation = obfuscation,
            },
            [hostPeer]);
        configuration.Validate();

        var code = VerificationCode.Compute(offer.Signer, _identity.PeerId, offer.Body.OfferId);
        var answerBody = new TunnelAnswerBody
        {
            OfferId = offer.Body.OfferId,
            ParticipantName = participantName,
            ParticipantCertificate = Convert.ToBase64String(_identity.PublicCertificateDer),
            ParticipantPublicKey = keys.PublicKey,
            ParticipantEndpoints = publicEndpoints.Select(e => e with { Port = listenPort }).Select(e => e.ToString()).ToArray(),
            EphemeralKey = EphemeralPublicKey(ephemeral),
        };

        var answerText = TunnelPackageCodec.Encode(TunnelPackageCodec.AnswerKind, answerBody, _identity);
        return new ParticipantAcceptance(answerText, configuration, offer.Signer, offer.Body.HostName, offer.Body.HostAddress, code);
    }

    /// <summary>
    /// Completes an offer on the host side: verifies the answer against the kept secrets and builds the participant
    /// peer for the host configuration.
    /// </summary>
    /// <param name="secrets">The secrets of the offer this answer belongs to.</param>
    /// <param name="answerText">The answer package.</param>
    /// <returns>The completion.</returns>
    /// <exception cref="FormatException">The answer is invalid, belongs to another offer, or the offer is expired.</exception>
    public HostCompletion CompleteOffer(PendingOfferSecrets secrets, string answerText)
    {
        ArgumentNullException.ThrowIfNull(secrets);

        var answer = TunnelPackageCodec.Decode<TunnelAnswerBody>(TunnelPackageCodec.AnswerKind, answerText, b => b.ParticipantCertificate);
        if (answer.Body.OfferId != secrets.OfferId)
        {
            throw new FormatException("Ответ относится к другому предложению.");
        }

        if (secrets.ExpiresAt <= _time.GetUtcNow())
        {
            throw new FormatException("Срок действия предложения истёк.");
        }

        using var ephemeral = ImportEphemeralPrivateKey(secrets.EphemeralPrivateKey);
        var preshared = PresharedKey(ephemeral, answer.Body.EphemeralKey);

        var peer = new AwgPeer
        {
            Comment = answer.Body.ParticipantName,
            PublicKey = answer.Body.ParticipantPublicKey,
            PresharedKey = preshared,
            AllowedIps = [$"{secrets.AssignedAddress}/32"],
            Endpoint = FirstEndpoint(answer.Body.ParticipantEndpoints),
            PersistentKeepalive = HostTunnelInterface.KeepaliveSeconds,
        };

        var code = VerificationCode.Compute(answer.Signer, _identity.PeerId, secrets.OfferId);
        return new HostCompletion(peer, answer.Signer, answer.Body.ParticipantName, code);
    }

    /// <summary>
    /// Creates an ephemeral P-256 key pair for the preshared key agreement.
    /// </summary>
    /// <returns>The key pair; dispose it after use.</returns>
    private static ECDiffieHellman NewEphemeralKey() => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>
    /// Returns the PKCS#8 private key of an ephemeral key.
    /// </summary>
    /// <param name="key">The ephemeral key.</param>
    /// <returns>The PKCS#8 bytes.</returns>
    private static byte[] EphemeralPrivateKey(ECDiffieHellman key) => key.ExportPkcs8PrivateKey();

    /// <summary>
    /// Returns the base64 SubjectPublicKeyInfo of an ephemeral key.
    /// </summary>
    /// <param name="key">The ephemeral key.</param>
    /// <returns>The base64 value.</returns>
    private static string EphemeralPublicKey(ECDiffieHellman key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    /// <summary>
    /// Imports a PKCS#8 ephemeral private key.
    /// </summary>
    /// <param name="privateKeyPkcs8">The PKCS#8 bytes.</param>
    /// <returns>The ephemeral key; dispose it after use.</returns>
    private static ECDiffieHellman ImportEphemeralPrivateKey(byte[] privateKeyPkcs8)
    {
        var key = ECDiffieHellman.Create();
        try
        {
            key.ImportPkcs8PrivateKey(privateKeyPkcs8, out _);
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Agrees the preshared key: the SHA-256 of the ECDH P-256 shared secret, in base64 (32 bytes, the format
    /// AmneziaWG expects).
    /// </summary>
    /// <param name="key">The local ephemeral key.</param>
    /// <param name="otherPublicKey">The base64 SubjectPublicKeyInfo of the other side.</param>
    /// <returns>The base64 preshared key.</returns>
    private static string PresharedKey(ECDiffieHellman key, string otherPublicKey)
    {
        var der = Convert.FromBase64String(otherPublicKey);
        using var other = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        other.ImportSubjectPublicKeyInfo(der, out _);
        var secret = key.DeriveRawSecretAgreement(other.PublicKey);
        try
        {
            return Convert.ToBase64String(SHA256.HashData(secret));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    /// <summary>
    /// Returns the first valid endpoint of a package, or <see langword="null"/>.
    /// </summary>
    /// <param name="endpoints">The endpoints as <c>host:port</c> text.</param>
    /// <returns>The endpoint.</returns>
    private static PeerEndpoint? FirstEndpoint(IReadOnlyList<string> endpoints)
    {
        foreach (var text in endpoints)
        {
            if (PeerEndpoint.TryParse(text, out var endpoint))
            {
                return endpoint;
            }
        }

        return null;
    }
}
