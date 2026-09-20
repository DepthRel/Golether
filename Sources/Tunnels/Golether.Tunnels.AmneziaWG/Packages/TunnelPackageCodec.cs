using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Golether.Security.Identity;

namespace Golether.Tunnels.AmneziaWG.Packages;

/// <summary>
/// Encodes packages as <c>golether-awg:&lt;kind&gt;:v1:&lt;body&gt;.&lt;signature&gt;</c> text the users pass on.
/// </summary>
/// <remarks>
/// The body is base64url JSON; the signature is ECDSA P-256 of the device over the prefix and the encoded body, so a
/// package of one kind cannot be replayed as another. The signing certificate is embedded in the body.
/// </remarks>
public static class TunnelPackageCodec
{
    /// <summary>
    /// The kind of offers.
    /// </summary>
    public const string OfferKind = "offer";

    /// <summary>
    /// The kind of answers.
    /// </summary>
    public const string AnswerKind = "answer";

    /// <summary>
    /// The maximum package length.
    /// </summary>
    public const int MaxLength = 16 * 1024;

    /// <summary>
    /// The JSON options of package bodies.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { MaxDepth = 8 };

    /// <summary>
    /// Returns the prefix of a package kind.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The prefix.</returns>
    public static string Prefix(string kind) => $"golether-awg:{kind}:v1:";

    /// <summary>
    /// Encodes and signs a package.
    /// </summary>
    /// <typeparam name="T">The body type.</typeparam>
    /// <param name="kind">The package kind.</param>
    /// <param name="body">The body.</param>
    /// <param name="identity">The signing device.</param>
    /// <returns>The package text.</returns>
    public static string Encode<T>(string kind, T body, DeviceIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var signed = Prefix(kind) + Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions));
        var signature = identity.Sign(Encoding.ASCII.GetBytes(signed));
        return signed + "." + Base64Url.EncodeToString(signature);
    }

    /// <summary>
    /// Decodes a package and verifies its signature.
    /// </summary>
    /// <typeparam name="T">The body type.</typeparam>
    /// <param name="kind">The expected kind.</param>
    /// <param name="text">The package text; whitespace and line breaks inside are ignored.</param>
    /// <param name="certificate">Returns the base64 signer certificate of a decoded body.</param>
    /// <returns>The verified package.</returns>
    /// <exception cref="FormatException">The package is malformed or the signature is invalid.</exception>
    public static SignedPackage<T> Decode<T>(string kind, string text, Func<T, string> certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var compact = new string((text ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());
        var prefix = Prefix(kind);
        if (compact.Length > MaxLength || !compact.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new FormatException(kind == OfferKind ? "Это не пакет-предложение Golether." : "Это не пакет-ответ Golether.");
        }

        var dot = compact.LastIndexOf('.');
        if (dot <= prefix.Length)
        {
            throw new FormatException("Пакет повреждён: нет подписи.");
        }

        T? body;
        byte[] signature;
        try
        {
            body = JsonSerializer.Deserialize<T>(Base64Url.DecodeFromChars(compact.AsSpan(prefix.Length, dot - prefix.Length)), JsonOptions);
            signature = Base64Url.DecodeFromChars(compact.AsSpan(dot + 1));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new FormatException("Пакет повреждён.", ex);
        }

        if (body is null)
        {
            throw new FormatException("Пакет пуст.");
        }

        byte[] certificateDer;
        try
        {
            certificateDer = Convert.FromBase64String(certificate(body) ?? string.Empty);
        }
        catch (FormatException ex)
        {
            throw new FormatException("Пакет содержит неверный сертификат.", ex);
        }

        if (!PeerCertificates.TryVerify(certificateDer, Encoding.ASCII.GetBytes(compact[..dot]), signature, out var signer))
        {
            throw new FormatException("Подпись пакета неверна: пакет изменён или повреждён.");
        }

        return new SignedPackage<T>(body, signer);
    }
}
