using System.Security.Cryptography;
using System.Text;

namespace Golether.Security.Invites;

/// <summary>
/// The result of checking an invitation token.
/// </summary>
public enum InviteCheckResult
{
    /// <summary>
    /// The token is valid and has been consumed.
    /// </summary>
    Accepted = 0,

    /// <summary>
    /// The token is unknown or was already used.
    /// </summary>
    Unknown = 1,

    /// <summary>
    /// The token has expired.
    /// </summary>
    Expired = 2,
}

/// <summary>
/// Tokens issued by the host: each token admits at most one connection attempt and expires after its lifetime.
/// </summary>
/// <remarks>
/// Only SHA-256 hashes of tokens are kept in memory. The registry is thread-safe.
/// </remarks>
public sealed class InviteRegistry
{
    /// <summary>
    /// The default lifetime of an invitation: 15 minutes.
    /// </summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The maximum lifetime of an invitation: 7 days.
    /// </summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(7);

    /// <summary>
    /// Expiry times by token hash.
    /// </summary>
    private readonly Dictionary<string, DateTimeOffset> _tokens = new(StringComparer.Ordinal);

    /// <summary>
    /// Guards <see cref="_tokens"/>.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The time provider.
    /// </summary>
    private readonly TimeProvider _timeProvider;

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
    /// Issues a new token.
    /// </summary>
    /// <param name="lifetime">The lifetime; <see cref="DefaultLifetime"/> when <see langword="null"/>.</param>
    /// <returns>The token and its expiry time.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The lifetime is not positive or exceeds <see cref="MaxLifetime"/>.</exception>
    public (string Token, DateTimeOffset ExpiresAt) Issue(TimeSpan? lifetime = null)
    {
        var effective = lifetime ?? DefaultLifetime;
        if (effective <= TimeSpan.Zero || effective > MaxLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "The invitation lifetime must be between 1 second and 7 days.");
        }

        var token = Invite.CreateToken();
        var expires = _timeProvider.GetUtcNow() + effective;
        lock (_gate)
        {
            _tokens[Hash(token)] = expires;
        }

        return (token, expires);
    }

    /// <summary>
    /// Checks a token presented by a connecting peer and consumes it when valid.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <returns>The check result.</returns>
    public InviteCheckResult TryConsume(string? token)
    {
        if (!Invite.IsValidToken(token))
        {
            return InviteCheckResult.Unknown;
        }

        var key = Hash(token!);
        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            if (!_tokens.Remove(key, out var expires))
            {
                return InviteCheckResult.Unknown;
            }

            return expires <= now ? InviteCheckResult.Expired : InviteCheckResult.Accepted;
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
        foreach (var key in _tokens.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
        {
            _tokens.Remove(key);
        }
    }
}
