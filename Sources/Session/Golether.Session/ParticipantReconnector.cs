using System.Globalization;
using Microsoft.Extensions.Logging;
using Golether.Localization;

namespace Golether.Session;

/// <summary>
/// Brings a participant back after the connection to the host broke: the ticket from the session is used, so no new
/// invitation is needed, and the host is asked again on every attempt.
/// </summary>
public sealed class ParticipantReconnector
{
    /// <summary>
    /// The number of attempts.
    /// </summary>
    private const int ATTEMPTS = 8;

    /// <summary>
    /// The waits before the attempts; the last one repeats.
    /// </summary>
    private static readonly TimeSpan[] _delays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(30),
    ];

    /// <summary>
    /// The application that holds the session.
    /// </summary>
    private readonly IReconnectionClient _client;

    /// <summary>
    /// The clock of the waits.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<ParticipantReconnector> _logger;

    /// <summary>
    /// Ends the current wait.
    /// </summary>
    private readonly SemaphoreSlim _retry = new(0, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="ParticipantReconnector"/> class.
    /// </summary>
    /// <param name="client">The application that holds the session.</param>
    /// <param name="timeProvider">The clock of the waits.</param>
    /// <param name="logger">The logger.</param>
    public ParticipantReconnector(IReconnectionClient client, TimeProvider timeProvider, ILogger<ParticipantReconnector> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Starts the next attempt without waiting any longer: the network is back. Kept for the next wait when no wait runs.
    /// </summary>
    public void Retry()
    {
        try
        {
            _retry.Release();
        }
        catch (SemaphoreFullException)
        {
            // One signal is enough for the next attempt.
        }
    }

    /// <summary>
    /// Tries to come back to the session, waiting longer after every failed attempt.
    /// </summary>
    /// <param name="ended">The session that ended.</param>
    /// <param name="cancellationToken">Stops the attempts.</param>
    /// <returns><see langword="true"/> when the session is back.</returns>
    /// <exception cref="OperationCanceledException">The attempts were stopped.</exception>
    public async Task<bool> RunAsync(ParticipantSession ended, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ended);

        for (var attempt = 1; attempt <= ATTEMPTS && !cancellationToken.IsCancellationRequested; attempt++)
        {
            var delay = _delays[Math.Min(attempt - 1, _delays.Length - 1)];
            _client.Report(Texts.Format("Session.Reconnect.Lost", attempt, ATTEMPTS, delay.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)));
            await WaitAsync(delay, cancellationToken).ConfigureAwait(false);

            if (_client.GetTicket(ended) is not { } ticket)
            {
                return false;
            }

            _client.Report(Texts.Format("Session.Reconnect.Trying", attempt, ATTEMPTS));

            // The ended session shares the player and the cameras with the new one. It is released before the new
            // one starts: released afterwards, its shutdown would stop the film and the cameras the new session has
            // just started.
            await ended.DisposeAsync().ConfigureAwait(false);
            var session = _client.CreateSession();
            try
            {
                await session.RejoinAsync(ticket, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
            {
                // Whatever went wrong, the half-open session is released: left behind, it would keep its connection
                // and its loops for ever.
                var refused = session.ClosedByHost;
                await session.DisposeAsync().ConfigureAwait(false);
                _logger.LogInformation("Reconnect attempt {Attempt} failed: {Error}", attempt, ex.Message);
                if (refused)
                {
                    // The host answered and said no: asking again would only bother it.
                    break;
                }

                continue;
            }
            catch (OperationCanceledException)
            {
                // The user left while the attempt was running: the half-open session must not stay behind.
                await session.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            if (!await _client.AdoptAsync(ended, session, ticket, cancellationToken).ConfigureAwait(false))
            {
                // The user left while the host was admitting this device again.
                await session.DisposeAsync().ConfigureAwait(false);
                return false;
            }

            _client.Report(Texts.Get("Session.Reconnect.Restored"));
            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();

        _client.Report(Texts.Get("Session.Reconnect.Failed"));
        return false;
    }

    /// <summary>
    /// Waits before an attempt, until the time is up or <see cref="Retry"/> ends the wait.
    /// </summary>
    /// <param name="delay">The wait.</param>
    /// <param name="cancellationToken">Stops the attempts.</param>
    /// <returns>A task that completes when the next attempt may start.</returns>
    private async Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var retry = _retry.WaitAsync(wait.Token);
        var time = Task.Delay(delay, _timeProvider, wait.Token);

        await Task.WhenAny(retry, time).ConfigureAwait(false);
        await wait.CancelAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
