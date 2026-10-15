using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Golether.Core.Enums;
using Golether.Core.Networking;
using Golether.Transports.Rendezvous.Protocol;
using Golether.Transports.Rendezvous.Punching;

namespace Golether.Transports.Rendezvous;

/// <summary>
/// The host's side of meetings: holds a room on the rendezvous server while the session runs and punches a TCP
/// connection to every participant the server announces.
/// </summary>
/// <remarks>
/// The host proves with its device key that the room is its own, keeps the connection alive with pings and comes back
/// after losing it, waiting longer after every failure. The connections it punches are handed over unauthenticated:
/// the listener of the session runs the TLS handshake and the admission as for any other connection.
/// </remarks>
public sealed class RendezvousHost : IAsyncDisposable
{
    /// <summary>
    /// The owner of the room.
    /// </summary>
    private readonly RendezvousOwner _owner;

    /// <summary>
    /// Receives the punched connections.
    /// </summary>
    private readonly Action<Socket> _connected;

    /// <summary>
    /// The options.
    /// </summary>
    private readonly RendezvousClientOptions _options;

    /// <summary>
    /// The time provider for the waits between attempts.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Limits the meetings answered at once.
    /// </summary>
    private readonly SemaphoreSlim _answerSlots;

    /// <summary>
    /// Stops the host.
    /// </summary>
    private readonly CancellationTokenSource _stopping = new();

    /// <summary>
    /// The registration loop, or <see langword="null"/> before <see cref="Start"/>.
    /// </summary>
    private Task? _loop;

    /// <summary>
    /// Initializes a new instance of the <see cref="RendezvousHost"/> class.
    /// </summary>
    /// <param name="point">The server and the room.</param>
    /// <param name="owner">The device key the room is held with.</param>
    /// <param name="connected">Receives every punched connection; it owns the socket from then on.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <param name="timeProvider">The time provider, or <see langword="null"/> for the system clock.</param>
    /// <param name="logger">The logger.</param>
    public RendezvousHost(
        RendezvousPoint point,
        RendezvousOwner owner,
        Action<Socket> connected,
        RendezvousClientOptions? options = null,
        TimeProvider? timeProvider = null,
        ILogger<RendezvousHost>? logger = null)
    {
        Point = point ?? throw new ArgumentNullException(nameof(point));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _connected = connected ?? throw new ArgumentNullException(nameof(connected));
        _options = options ?? new RendezvousClientOptions();
        _options.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<RendezvousHost>.Instance;
        _answerSlots = new SemaphoreSlim(_options.MaxConcurrentAnswers, _options.MaxConcurrentAnswers);
    }

    /// <summary>
    /// Raised when the room is taken or lost. May be raised on any thread.
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Gets the server and the room.
    /// </summary>
    public RendezvousPoint Point { get; }

    /// <summary>
    /// Gets a value indicating whether the room is held right now.
    /// </summary>
    public bool IsRegistered { get; private set; }

    /// <summary>
    /// Gets the address of this device as the server saw it, or <see langword="null"/> before the first registration.
    /// </summary>
    public string? ObservedAddress { get; private set; }

    /// <summary>
    /// Gets why the last attempt failed, or <see langword="null"/> while the room is held.
    /// </summary>
    public RendezvousError? LastError { get; private set; }

    /// <summary>
    /// Starts holding the room in the background.
    /// </summary>
    public void Start()
    {
        _loop ??= Task.Run(() => RunAsync(_stopping.Token));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The loop died of something unexpected earlier; ending the session must still release the rest.
                _logger.LogWarning(ex, "The registration on {Server} had stopped with an error", Point.Server);
            }
        }

        _stopping.Dispose();
    }

    /// <summary>
    /// Holds the room and comes back after every loss until stopped.
    /// </summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A task that completes when stopped.</returns>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            RendezvousError error;
            try
            {
                await HoldRoomAsync(() => failures = 0, cancellationToken).ConfigureAwait(false);
                error = RendezvousError.Unreachable;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (RendezvousException ex)
            {
                error = ex.Error;
                _logger.LogInformation("The rendezvous server {Server} is not available: {Error}", Point.Server, ex.Error);
            }

            SetState(false, error);
            var delay = _options.RetryDelays[Math.Min(failures++, _options.RetryDelays.Count - 1)];
            try
            {
                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        SetState(false, null);
    }

    /// <summary>
    /// Registers the room and serves the announcements until the connection ends.
    /// </summary>
    /// <param name="registered">Called once the room is held.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the server closed the connection.</returns>
    /// <exception cref="RendezvousException">The server refused or the connection broke.</exception>
    private async Task HoldRoomAsync(Action registered, CancellationToken cancellationToken)
    {
        await using var connection = await RendezvousConnection.OpenAsync(Point.Server, _options.ConnectTimeout, cancellationToken).ConfigureAwait(false);
        var signature = _owner.Sign(RendezvousSignature.BuildPayload(connection.Nonce, Point.Room));
        await RendezvousDialer.SendAsync(
            connection,
            new RegisterMessage(Point.Room, Convert.ToBase64String(_owner.Certificate), Convert.ToBase64String(signature)),
            cancellationToken).ConfigureAwait(false);
        switch (await connection.ReceiveAsync(_options.ConnectTimeout, cancellationToken).ConfigureAwait(false))
        {
            case RegisteredMessage done:
                ObservedAddress = done.Observed;
                break;
            case ErrorMessage refused:
                throw new RendezvousException(refused.Error);
            case null:
                throw new RendezvousException(RendezvousError.Unreachable);
            default:
                throw new RendezvousException(RendezvousError.BadRequest);
        }

        registered();
        SetState(true, null);
        _logger.LogInformation("Holding a room on {Server}, seen as {Observed}", Point.Server, ObservedAddress);
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var keepAlive = KeepAliveAsync(connection, session.Token);
        try
        {
            while (true)
            {
                // The server answers every ping, so three silent intervals mean the connection is gone.
                var message = await connection.ReceiveAsync(_options.KeepAliveInterval * 3, cancellationToken).ConfigureAwait(false);
                switch (message)
                {
                    case null:
                        return;
                    case IncomingMessage incoming when RendezvousProtocol.IsValidRandomId(incoming.Punch):
                        Answer(incoming.Punch, cancellationToken);
                        break;
                    case ErrorMessage refused:
                        throw new RendezvousException(refused.Error);
                }
            }
        }
        finally
        {
            await session.CancelAsync().ConfigureAwait(false);
            await keepAlive.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Pings the server until the connection ends.
    /// </summary>
    /// <param name="connection">The registration connection.</param>
    /// <param name="cancellationToken">Stops the pings.</param>
    /// <returns>A task that completes when stopped.</returns>
    private async Task KeepAliveAsync(RendezvousConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(_options.KeepAliveInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
                await RendezvousDialer.SendAsync(connection, new PingMessage(), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or RendezvousException)
        {
            // The receive loop notices a dead connection by itself.
        }
    }

    /// <summary>
    /// Answers an announced meeting in the background, if a slot is free.
    /// </summary>
    /// <param name="punch">The identifier of the meeting.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private void Answer(string punch, CancellationToken cancellationToken)
    {
        if (!_answerSlots.Wait(0))
        {
            _logger.LogWarning("Ignored a meeting: {Count} are being answered already", _options.MaxConcurrentAnswers);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var socket = await AnswerAsync(punch, cancellationToken).ConfigureAwait(false);
                _connected(socket);
            }
            catch (RendezvousException ex)
            {
                _logger.LogInformation("A meeting through {Server} failed: {Error}", Point.Server, ex.Error);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _answerSlots.Release();
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// Tells the server where this side punches from and punches towards the participant.
    /// </summary>
    /// <param name="punch">The identifier of the meeting.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The connection on which the participant spoke first.</returns>
    /// <exception cref="RendezvousException">The meeting did not happen.</exception>
    private async Task<Socket> AnswerAsync(string punch, CancellationToken cancellationToken)
    {
        await using var connection = await RendezvousConnection.OpenAsync(Point.Server, _options.ConnectTimeout, cancellationToken).ConfigureAwait(false);
        await RendezvousDialer.SendAsync(connection, new AcceptMessage(punch, LocalAddresses.Describe(connection.LocalPort)), cancellationToken).ConfigureAwait(false);
        var targets = await connection.ReceivePunchAsync(_options.ConnectTimeout, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Punching from port {Port} towards {Targets}", connection.LocalPort, string.Join(", ", targets));
        return await TcpPunch.AnswerAsync(connection.LocalPort, targets, _options.PunchTimeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates the state and tells the subscribers when it changed.
    /// </summary>
    /// <param name="registered">Whether the room is held.</param>
    /// <param name="error">The reason of the failure, or <see langword="null"/>.</param>
    private void SetState(bool registered, RendezvousError? error)
    {
        var changed = registered != IsRegistered || error != LastError;
        IsRegistered = registered;
        LastError = error;
        if (changed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
