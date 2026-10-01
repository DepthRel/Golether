using Microsoft.Extensions.Logging;

namespace Golether.Transports.PortMapping;

/// <summary>
/// An open port mapping: renewed at half of its lease and removed on disposal.
/// </summary>
public sealed class PortMappingLease : IAsyncDisposable
{
    /// <summary>
    /// The protocol that opened the mapping.
    /// </summary>
    private readonly IPortMappingProtocol _protocol;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Stops the renewal.
    /// </summary>
    private readonly CancellationTokenSource _stop = new();

    /// <summary>
    /// The longest wait before a renewal.
    /// </summary>
    internal static readonly TimeSpan MaxRenewalDelay = TimeSpan.FromHours(12);

    /// <summary>
    /// The shortest wait before a renewal.
    /// </summary>
    internal static readonly TimeSpan MinRenewalDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The renewal loop.
    /// </summary>
    private readonly Task _renewal;

    /// <summary>
    /// Initializes a new instance of the <see cref="PortMappingLease"/> class.
    /// </summary>
    /// <param name="protocol">The protocol that opened the mapping.</param>
    /// <param name="mapping">The mapping.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    internal PortMappingLease(IPortMappingProtocol protocol, PortMappingResult mapping, TimeProvider timeProvider, ILogger logger)
    {
        _protocol = protocol;
        _logger = logger;
        Mapping = mapping;
        _renewal = mapping.Lifetime > TimeSpan.Zero ? RenewAsync(timeProvider) : Task.CompletedTask;
    }

    /// <summary>
    /// Gets the current mapping.
    /// </summary>
    public PortMappingResult Mapping { get; private set; }

    /// <summary>
    /// Stops the renewal and removes the mapping from the router.
    /// </summary>
    /// <returns>A task that completes when the router answered or 3 seconds passed.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        await _renewal.ConfigureAwait(false);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await _protocol.UnmapAsync(Mapping, timeout.Token).ConfigureAwait(false);
            _logger.LogInformation("Port {Port} closed on the router", Mapping.InternalPort);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogDebug(ex, "Closing port {Port} failed; the lease expires by itself", Mapping.InternalPort);
        }

        _stop.Dispose();
    }

    /// <summary>
    /// Returns the wait before the next renewal: half of the lifetime, kept between <see cref="MinRenewalDelay"/> and
    /// <see cref="MaxRenewalDelay"/>. The lifetime is what the router answered: half of a huge one is more than a
    /// delay can be (it would fail at once, again and again, without ever waiting), and half of a tiny one would keep
    /// the router busy.
    /// </summary>
    /// <param name="lifetime">The lifetime of the mapping.</param>
    /// <returns>The wait.</returns>
    internal static TimeSpan RenewalDelay(TimeSpan lifetime)
    {
        var delay = lifetime / 2;
        return delay > MaxRenewalDelay ? MaxRenewalDelay : delay < MinRenewalDelay ? MinRenewalDelay : delay;
    }

    /// <summary>
    /// Renews the mapping at half of its lease until disposal.
    /// </summary>
    /// <param name="timeProvider">The time provider.</param>
    /// <returns>A task that completes when the lease is disposed.</returns>
    private async Task RenewAsync(TimeProvider timeProvider)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(RenewalDelay(Mapping.Lifetime), timeProvider, _stop.Token).ConfigureAwait(false);
                var renewed = await _protocol.MapAsync(Mapping.InternalPort, PortMapper.DefaultLifetime, _stop.Token).ConfigureAwait(false);
                if (renewed is null)
                {
                    _logger.LogWarning("The router did not renew port {Port}; retrying later", Mapping.InternalPort);
                    continue;
                }

                Mapping = renewed;
                if (renewed.Lifetime <= TimeSpan.Zero)
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogWarning(ex, "Renewing port {Port} failed", Mapping.InternalPort);
            }
        }
    }
}
