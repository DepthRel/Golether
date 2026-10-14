using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.Transports.Relay;

/// <summary>
/// The participant's way to the relay of the host over one shared stream: every connection of the cameras and voices
/// becomes a channel of a <see cref="RelayMultiplexer"/>. The stream is opened when the first channel is asked for
/// and opened again when it broke.
/// </summary>
public sealed class SharedRelayRoute : IAsyncDisposable
{
    /// <summary>
    /// How long a failure to open the shared stream is remembered. The cameras ask for all their connections at once:
    /// without this each of them would repeat the whole failed attempt in turn.
    /// </summary>
    private static readonly TimeSpan FailureMemory = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Opens the shared stream to the host.
    /// </summary>
    private readonly Func<CancellationToken, Task<Stream>> _open;

    /// <summary>
    /// The time source.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Lets one caller at a time open the shared stream; the others wait for the result.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// The shared stream in use, or <see langword="null"/>.
    /// </summary>
    private RelayMultiplexer? _current;

    /// <summary>
    /// The moment until which a failed opening is not repeated, as a timestamp of <see cref="_timeProvider"/>.
    /// </summary>
    private long _failedAt;

    /// <summary>
    /// Why the last opening failed, or <see langword="null"/>.
    /// </summary>
    private string? _failure;

    /// <summary>
    /// Whether the route was closed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharedRelayRoute"/> class.
    /// </summary>
    /// <param name="open">Opens the shared stream to the host; the route owns the stream.</param>
    /// <param name="timeProvider">The time source.</param>
    /// <param name="logger">The logger.</param>
    public SharedRelayRoute(Func<CancellationToken, Task<Stream>> open, TimeProvider timeProvider, ILogger? logger = null)
    {
        _open = open ?? throw new ArgumentNullException(nameof(open));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Opens one connection to the relay of the host: a channel of the shared stream.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The channel.</returns>
    /// <exception cref="IOException">The host could not be reached.</exception>
    public async Task<Stream> OpenAsync(CancellationToken cancellationToken)
    {
        RelayMultiplexer shared;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_current is not { IsAlive: true })
            {
                if (_failure is { } failure && _timeProvider.GetElapsedTime(_failedAt) < FailureMemory)
                {
                    throw new IOException(failure);
                }

                if (_current is { } broken)
                {
                    await broken.DisposeAsync().ConfigureAwait(false);
                    _current = null;
                }

                try
                {
                    var stream = await _open(cancellationToken).ConfigureAwait(false);
                    _current = new RelayMultiplexer(stream, accept: null, _logger);
                    _current.Start();
                    _failure = null;
                    _logger.LogInformation("Opened the shared stream to the relay of the host");
                }
                catch (IOException ex)
                {
                    _failure = ex.Message;
                    _failedAt = _timeProvider.GetTimestamp();
                    throw;
                }
            }

            shared = _current;
        }
        finally
        {
            _gate.Release();
        }

        return await shared.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            if (_current is { } shared)
            {
                _current = null;
                await shared.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
