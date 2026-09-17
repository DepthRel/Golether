using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Golether.Core.Identity;
using Golether.Core.Networking;

namespace Golether.Security.Invites;

/// <summary>
/// An invitation to a session, passed to a participant through any channel (messenger, e-mail, QR code).
/// </summary>
/// <remarks>
/// The link carries the host identifier (pinned by the participant during the TLS handshake), candidate addresses and a
/// one-time token. A token alone is not enough: the host approves every newcomer.
/// </remarks>
public sealed record Invite
{
    /// <summary>
    /// The link prefix.
    /// </summary>
    public const string LinkPrefix = "golether://join/v1/";

    /// <summary>
    /// The maximum length of a link.
    /// </summary>
    public const int MaxLinkLength = 4096;

    /// <summary>
    /// The maximum number of candidate endpoints.
    /// </summary>
    public const int MaxEndpoints = 16;

    /// <summary>
    /// The number of random bytes in a token (128 bits).
    /// </summary>
    public const int TokenBytes = 16;

    /// <summary>
    /// The JSON options of the link payload.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Gets the host identifier.
    /// </summary>
    public required PeerId HostPeerId { get; init; }

    /// <summary>
    /// Gets the host display name.
    /// </summary>
    public required string HostName { get; init; }

    /// <summary>
    /// Gets the session name.
    /// </summary>
    public required string SessionName { get; init; }

    /// <summary>
    /// Gets the candidate host endpoints in order of preference.
    /// </summary>
    public required IReadOnlyList<PeerEndpoint> Endpoints { get; init; }

    /// <summary>
    /// Gets the one-time token (base64url, <see cref="TokenBytes"/> bytes).
    /// </summary>
    public required string Token { get; init; }

    /// <summary>
    /// Gets the expiry time.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Creates a random token.
    /// </summary>
    /// <returns>The base64url token.</returns>
    public static string CreateToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));

    /// <summary>
    /// Formats the invitation as a link.
    /// </summary>
    /// <returns>The <c>golether://join/v1/...</c> link.</returns>
    public string ToLink()
    {
        var payload = new InvitePayload(
            HostPeerId.Value,
            HostName,
            SessionName,
            Endpoints.Select(e => e.ToString()).ToArray(),
            Token,
            ExpiresAt.ToUnixTimeSeconds());
        return LinkPrefix + Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
    }

    /// <summary>
    /// Parses and validates a link.
    /// </summary>
    /// <param name="link">The link; surrounding whitespace is ignored.</param>
    /// <returns>The invitation.</returns>
    /// <exception cref="FormatException">The link is malformed.</exception>
    public static Invite ParseLink(string link)
    {
        var text = (link ?? string.Empty).Trim();
        if (text.Length > MaxLinkLength || !text.StartsWith(LinkPrefix, StringComparison.Ordinal))
        {
            throw new FormatException("Это не приглашение Golether.");
        }

        InvitePayload? payload;
        try
        {
            var bytes = Base64Url.DecodeFromChars(text.AsSpan(LinkPrefix.Length));
            payload = JsonSerializer.Deserialize<InvitePayload>(bytes, JsonOptions);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new FormatException("Приглашение повреждено.", ex);
        }

        if (payload is null
            || !PeerId.TryParse(payload.Host, out var host)
            || payload.Endpoints is null || payload.Endpoints.Length is 0 or > MaxEndpoints
            || !IsValidToken(payload.Token))
        {
            throw new FormatException("Приглашение повреждено.");
        }

        var endpoints = new List<PeerEndpoint>(payload.Endpoints.Length);
        foreach (var item in payload.Endpoints)
        {
            if (!PeerEndpoint.TryParse(item, out var endpoint))
            {
                throw new FormatException($"Приглашение содержит неверный адрес '{item}'.");
            }

            endpoints.Add(endpoint);
        }

        return new Invite
        {
            HostPeerId = host,
            HostName = Core.Session.ParticipantInfo.NormalizeDisplayName(payload.Name),
            SessionName = Core.Session.ParticipantInfo.NormalizeDisplayName(payload.Session),
            Endpoints = endpoints,
            Token = payload.Token!,
            ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.Expires),
        };
    }

    /// <summary>
    /// Checks the token format.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <returns><see langword="true"/> when the token decodes to <see cref="TokenBytes"/> bytes.</returns>
    public static bool IsValidToken(string? token)
    {
        // 16 bytes are 22 base64url characters without padding. Base64Url throws on malformed input, so the alphabet
        // is checked first.
        if (token is not { Length: 22 } || !token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[32];
        return Base64Url.TryDecodeFromChars(token, buffer, out var written) && written == TokenBytes;
    }

    /// <summary>
    /// The JSON payload of a link with short property names.
    /// </summary>
    /// <param name="Host">The host identifier.</param>
    /// <param name="Name">The host display name.</param>
    /// <param name="Session">The session name.</param>
    /// <param name="Endpoints">The candidate endpoints.</param>
    /// <param name="Token">The one-time token.</param>
    /// <param name="Expires">The expiry time in Unix seconds.</param>
    private sealed record InvitePayload(string? Host, string? Name, string? Session, string[]? Endpoints, string? Token, long Expires);
}
