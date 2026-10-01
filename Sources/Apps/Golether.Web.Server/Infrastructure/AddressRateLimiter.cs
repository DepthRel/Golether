using System.Net;

namespace Golether.Web.Server.Infrastructure;

/// <summary>
/// Counts the requests of every IP address in one-minute windows and refuses those over the limit.
/// </summary>
internal sealed class AddressRateLimiter
{
    /// <summary>
    /// The length of a window.
    /// </summary>
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The number of tracked addresses after which the stale ones are dropped.
    /// </summary>
    private const int CleanupThreshold = 10_000;

    /// <summary>
    /// The largest number of tracked addresses. UDP senders can forge their address, so without a ceiling a flood of
    /// forged requests would grow the table without end; over it, addresses not seen yet are refused until old ones
    /// expire.
    /// </summary>
    internal const int MaxTracked = 200_000;

    /// <summary>
    /// The shortest time between two sweeps of stale addresses: a sweep walks the whole table, and a full table must
    /// not make every request pay for it.
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The requests allowed per window.
    /// </summary>
    private readonly int _perMinute;

    /// <summary>
    /// The time provider.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The window start and count of every address.
    /// </summary>
    private readonly Dictionary<IPAddress, (DateTimeOffset Start, int Count)> _windows = [];

    /// <summary>
    /// Guards <see cref="_windows"/>.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The time of the last sweep of stale addresses.
    /// </summary>
    private DateTimeOffset _lastSweep = DateTimeOffset.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddressRateLimiter"/> class.
    /// </summary>
    /// <param name="perMinute">The requests allowed per minute and address.</param>
    /// <param name="timeProvider">The time provider.</param>
    public AddressRateLimiter(int perMinute, TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(perMinute, 1);
        _perMinute = perMinute;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Counts a request.
    /// </summary>
    /// <param name="address">The address of the requester.</param>
    /// <returns><see langword="false"/> when the address is over the limit.</returns>
    public bool TryAcquire(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            if (_windows.Count >= CleanupThreshold && now - _lastSweep >= SweepInterval)
            {
                _lastSweep = now;
                foreach (var stale in _windows.Where(w => now - w.Value.Start >= Window).Select(w => w.Key).ToList())
                {
                    _windows.Remove(stale);
                }
            }

            var known = _windows.TryGetValue(address, out var current);
            var window = known && now - current.Start < Window ? current : (Start: now, Count: 0);
            if (window.Count >= _perMinute || (!known && _windows.Count >= MaxTracked))
            {
                return false;
            }

            _windows[address] = (window.Start, window.Count + 1);
            return true;
        }
    }
}
