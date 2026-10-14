using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Golether.Core.Configuration;
using Golether.Core.Identity;
using Golether.Localization;
using Golether.Core.Networking;

namespace Golether.Security.Invites;

/// <summary>
/// An invitation to a session, passed to a participant through any channel (messenger, e-mail, QR code).
/// </summary>
/// <remarks>
/// The link carries the host identifier (pinned by the participant during the TLS handshake), candidate addresses and a
/// one-time token. A token alone is not enough: the host approves every newcomer.
/// <para>
/// One invitation has two spellings with the same content: the key (<see cref="LinkPrefix"/>), which is pasted into
/// the application, and the web link (<see cref="WebLinkPrefix"/>), which any messenger makes clickable and whose
/// page hands the key over to the application.
/// </para>
/// </remarks>
public sealed record Invite
{
    /// <summary>
    /// The link prefix.
    /// </summary>
    public const string LinkPrefix = "golether://join/v1/";

    /// <summary>
    /// The prefix of the web link: the page of the project, with the invitation after the <c>#</c>.
    /// </summary>
    public const string WebLinkPrefix = ProjectSite.JoinUrlPrefix;

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
    /// The latest expiry time a link may carry, in Unix seconds (the end of year 9999): a larger number cannot be
    /// turned into a date and would fail instead of being reported as a damaged link.
    /// </summary>
    private const long MaxExpiresSeconds = 253_402_300_799;

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
    /// Gets the rendezvous server and the room of the host, or <see langword="null"/> when the host uses none.
    /// Participants whose direct attempts fail meet the host through it.
    /// </summary>
    public RendezvousPoint? Rendezvous { get; init; }

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
            ExpiresAt.ToUnixTimeSeconds(),
            Rendezvous?.Server.ToString(),
            Rendezvous?.Room);
        return LinkPrefix + Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
    }

    /// <summary>
    /// Formats the invitation as a web link that opens the application from a browser.
    /// </summary>
    /// <returns>The <c>https://…/join/#…</c> link with the same content as <see cref="ToLink"/>.</returns>
    public string ToWebLink() => WebLinkPrefix + ToLink()[LinkPrefix.Length..];

    /// <summary>
    /// Tells whether a text looks like an invitation in either spelling, without checking its content.
    /// </summary>
    /// <param name="text">The text, for example a command-line argument.</param>
    /// <returns><see langword="true"/> when the text begins like a key or a web link.</returns>
    public static bool LooksLikeLink(string? text)
        => text is not null && FindPrefix(text.AsSpan().Trim()) is not null;

    /// <summary>
    /// Finds the prefix a link begins with.
    /// </summary>
    /// <param name="text">The trimmed link.</param>
    /// <returns>The prefix, or <see langword="null"/> when the text is no invitation.</returns>
    private static string? FindPrefix(ReadOnlySpan<char> text)
        => text.StartsWith(LinkPrefix, StringComparison.Ordinal) ? LinkPrefix
            : text.StartsWith(WebLinkPrefix, StringComparison.Ordinal) ? WebLinkPrefix
            : null;

    /// <summary>
    /// Parses and validates a link: the key or the web link.
    /// </summary>
    /// <param name="link">The link; surrounding whitespace is ignored.</param>
    /// <returns>The invitation.</returns>
    /// <exception cref="FormatException">The link is malformed.</exception>
    public static Invite ParseLink(string link)
    {
        var text = (link ?? string.Empty).Trim();
        if (text.Length > MaxLinkLength || FindPrefix(text) is not { } prefix)
        {
            throw new FormatException(Texts.Get("Invite.Error.NotInvite"));
        }

        InvitePayload? payload;
        try
        {
            var bytes = Base64Url.DecodeFromChars(text.AsSpan(prefix.Length));
            payload = JsonSerializer.Deserialize<InvitePayload>(bytes, JsonOptions);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new FormatException(Texts.Get("Invite.Error.Damaged"), ex);
        }

        if (payload is null
            || !PeerId.TryParse(payload.Host, out var host)
            || payload.Endpoints is null || payload.Endpoints.Length is 0 or > MaxEndpoints
            || !IsValidToken(payload.Token)
            || payload.Expires is < 0 or > MaxExpiresSeconds)
        {
            throw new FormatException(Texts.Get("Invite.Error.Damaged"));
        }

        var endpoints = new List<PeerEndpoint>(payload.Endpoints.Length);
        foreach (var item in payload.Endpoints)
        {
            if (!PeerEndpoint.TryParse(item, out var endpoint))
            {
                throw new FormatException(Texts.Format("Invite.Error.BadAddress", item));
            }

            endpoints.Add(endpoint);
        }

        RendezvousPoint? rendezvous = null;
        if ((payload.Meet is not null || payload.Room is not null) && !RendezvousPoint.TryCreate(payload.Meet, payload.Room, out rendezvous))
        {
            throw new FormatException(Texts.Get("Invite.Error.Damaged"));
        }

        return new Invite
        {
            HostPeerId = host,
            HostName = Core.Session.ParticipantInfo.NormalizeDisplayName(payload.Name),
            SessionName = Core.Session.ParticipantInfo.NormalizeDisplayName(payload.Session),
            Endpoints = endpoints,
            Token = payload.Token!,
            ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.Expires),
            Rendezvous = rendezvous,
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
    /// <remarks>
    /// The token and the expiry time are written first. Everything else is the same in every invitation of a session,
    /// so with them in front a new invitation looks new from its first characters, which is all a narrow field
    /// shows. The order means nothing to a reader: old and new versions read each other's links.
    /// </remarks>
    /// <param name="Host">The host identifier.</param>
    /// <param name="Name">The host display name.</param>
    /// <param name="Session">The session name.</param>
    /// <param name="Endpoints">The candidate endpoints.</param>
    /// <param name="Token">The one-time token.</param>
    /// <param name="Expires">The expiry time in Unix seconds.</param>
    /// <param name="Meet">The rendezvous server as <c>host:port</c>; absent without one and in older links.</param>
    /// <param name="Room">The room of the host on the rendezvous server; absent without one.</param>
    private sealed record InvitePayload(
        string? Host,
        string? Name,
        string? Session,
        string[]? Endpoints,
        [property: JsonPropertyOrder(-2)] string? Token,
        [property: JsonPropertyOrder(-1)] long Expires,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Meet = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Room = null);
}
