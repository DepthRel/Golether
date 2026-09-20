using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.Transports.PortMapping;

/// <summary>
/// Opens a port on the router with the first protocol that works and keeps the mapping alive.
/// </summary>
public sealed class PortMapper
{
    /// <summary>
    /// The requested lease: short, so a forgotten mapping disappears by itself.
    /// </summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(1);

    /// <summary>
    /// The protocols in order of preference.
    /// </summary>
    private readonly IReadOnlyList<IPortMappingProtocol> _protocols;

    /// <summary>
    /// The time provider.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PortMapper"/> class.
    /// </summary>
    /// <param name="protocols">The protocols in order of preference.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public PortMapper(IReadOnlyList<IPortMappingProtocol> protocols, TimeProvider timeProvider, ILogger<PortMapper>? logger = null)
    {
        _protocols = protocols ?? throw new ArgumentNullException(nameof(protocols));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? NullLogger<PortMapper>.Instance;
    }

    /// <summary>
    /// Creates a mapper with NAT-PMP and UPnP.
    /// </summary>
    /// <param name="http">The HTTP client for UPnP.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <returns>The mapper.</returns>
    public static PortMapper CreateDefault(HttpClient http, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        return new PortMapper(
            [
                new NatPmpClient(logger: loggerFactory.CreateLogger<NatPmpClient>()),
                new UpnpIgdClient(http, logger: loggerFactory.CreateLogger<UpnpIgdClient>()),
            ],
            TimeProvider.System,
            loggerFactory.CreateLogger<PortMapper>());
    }

    /// <summary>
    /// Opens a TCP port.
    /// </summary>
    /// <param name="port">The port on this device and on the router.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The lease that renews the mapping until disposed, or <see langword="null"/> when no router helped.</returns>
    public async Task<PortMappingLease?> MapAsync(int port, CancellationToken cancellationToken)
    {
        foreach (var protocol in _protocols)
        {
            PortMappingResult? result;
            try
            {
                result = await protocol.MapAsync(port, DefaultLifetime, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                _logger.LogDebug(ex, "{Protocol} failed", protocol.Name);
                continue;
            }

            if (result is not null)
            {
                _logger.LogInformation("Port {Port} opened with {Protocol}: {Address}:{External}", port, protocol.Name, result.ExternalAddress, result.ExternalPort);
                return new PortMappingLease(protocol, result, _timeProvider, _logger);
            }
        }

        _logger.LogInformation("No router opened port {Port}", port);
        return null;
    }
}
