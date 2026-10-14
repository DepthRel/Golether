using System.Security.Cryptography;
using System.Text;
using Golether.Core.Data.Enums;

namespace Golether.Security.Invites;

/// <summary>
/// Tokens issued by the host. One token lets in everybody the host sent it to until its lifetime ends: an invitation
/// is one link for the whole company, not a ticket per guest.
/// </summary>
/// <remarks>
/// A token alone admits nobody: the host approves every newcomer. What the registry bounds is how long an invitation
/// lives and how many requests it may bring (<see cref="MaxUses"/>), so a link that leaked cannot be used to pester
/// the host for ever. Every token also carries the number it was issued under; the host uses it to tell invitations
/// made before a device was removed from those made after (see <see cref="LastSequence"/>).
/// Only SHA-256 hashes of tokens are kept in memory. The registry is thread-safe.
/// </remarks>
public sealed class InviteRegistry
{
    /// <summary>
    /// The default lifetime of an invitation: 15 minutes.
    /// </summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The lifetime of the web link of an invitation: 15 minutes. A link is clicked right away, so it has no reason
    /// to live longer.
    /// </summary>
    public static readonly TimeSpan WebLinkLifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The maximum lifetime of an invitation: 1 hour. The lifetime limits only the moment of joining: a participant
    /// who was admitted stays in the session after the invitation expired.
    /// </summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(1);

    /// <summary>
    /// How many join requests one invitation may bring. A session holds five people, so an honest company never
    /// comes near it; a leaked link stops working after this many attempts.
    /// </summary>
    public const int MaxUses = 16;

    /// <summary>
    /// The issued tokens by token hash.
    /// </summary>
    private readonly Dictionary<string, Entry> _tokens = new(StringComparer.Ordinal);

    /// <summary>
    /// Guards <see cref="_tokens"/> and <see cref="_sequence"/>.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The time provider.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The number of the token issued last.
    /// </summary>
    private long _sequence;

    /// <summary>
    /// Initializes a new instance of the <see cref="InviteRegistry"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider.</param>
    public InviteRegistry(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Gets the number of active tokens.
    /// </summary>
    public int ActiveCount
    {
        get
        {
            lock (_gate)
            {
                RemoveExpired(_timeProvider.GetUtcNow());
                return _tokens.Count;
            }
        }
    }

    /// <summary>
    /// Gets the number of the token issued last, or 0 before the first one. Tokens are numbered in the order they are
    /// issued, so "issued after this moment" is "has a larger number".
    /// </summary>
    public long LastSequence
    {
        get
        {
            lock (_gate)
            {
                return _sequence;
            }
        }
    }

    /// <summary>
    /// Issues a new token.
    /// </summary>
    /// <param name="lifetime">The lifetime; <see cref="DefaultLifetime"/> when <see langword="null"/>.</param>
    /// <returns>The token and its expiry time.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The lifetime is not positive or exceeds <see cref="MaxLifetime"/>.</exception>
    /// <remarks>
    /// The same lifetime is written into the invitation, so the host and the participant agree on the moment it ends.
    /// </remarks>
    public (string Token, DateTimeOffset ExpiresAt) Issue(TimeSpan? lifetime = null)
    {
        var effective = lifetime ?? DefaultLifetime;
        if (effective <= TimeSpan.Zero || effective > MaxLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "The invitation lifetime must be between 1 second and 1 hour.");
        }

        var token = Invite.CreateToken();
        var expires = _timeProvider.GetUtcNow() + effective;
        lock (_gate)
        {
            _tokens[Hash(token)] = new Entry(expires, ++_sequence);
        }

        return (token, expires);
    }

    /// <summary>
    /// Checks a token presented by a connecting peer. A valid token stays valid for the next guest: it ends with its
    /// lifetime or after <see cref="MaxUses"/> requests, whichever comes first.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <param name="sequence">The number the token was issued under, or 0 when the token is not accepted.</param>
    /// <returns>The check result.</returns>
    public InviteCheckResult TryUse(string? token, out long sequence)
    {
        sequence = 0;
        if (!Invite.IsValidToken(token))
        {
            return InviteCheckResult.Unknown;
        }

        var key = Hash(token!);
        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            if (!_tokens.TryGetValue(key, out var entry))
            {
                return InviteCheckResult.Unknown;
            }

            if (entry.ExpiresAt <= now || entry.Uses >= MaxUses)
            {
                // Spent or out of time: forgotten, so the next attempt finds nothing at all.
                _tokens.Remove(key);
                return InviteCheckResult.Expired;
            }

            entry.Uses++;
            sequence = entry.Sequence;
            return InviteCheckResult.Accepted;
        }
    }

    /// <summary>
    /// Revokes a token.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <returns><see langword="true"/> when the token was active.</returns>
    public bool Revoke(string token)
    {
        lock (_gate)
        {
            return _tokens.Remove(Hash(token));
        }
    }

    /// <summary>
    /// Hashes a token.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <returns>The hexadecimal SHA-256.</returns>
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    /// <summary>
    /// Removes expired tokens; the caller holds the lock.
    /// </summary>
    /// <param name="now">The current time.</param>
    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var key in _tokens.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
        {
            _tokens.Remove(key);
        }
    }

    /// <summary>
    /// What the registry keeps about an issued token.
    /// </summary>
    /// <param name="expiresAt">The moment the token stops working.</param>
    /// <param name="sequence">The number the token was issued under.</param>
    private sealed class Entry(DateTimeOffset expiresAt, long sequence)
    {
        /// <summary>
        /// Gets the moment the token stops working.
        /// </summary>
        public DateTimeOffset ExpiresAt { get; } = expiresAt;

        /// <summary>
        /// Gets the number the token was issued under.
        /// </summary>
        public long Sequence { get; } = sequence;

        /// <summary>
        /// Gets or sets how many join requests the token has brought.
        /// </summary>
        public int Uses { get; set; }
    }
}
