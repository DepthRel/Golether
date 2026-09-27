using System.Net;
using System.Net.Sockets;
using Golether.Core.Networking;
using Golether.Transports.Rendezvous.Protocol;
using Golether.Web.Server.Infrastructure;
using Golether.Web.Server.Options;

namespace Golether.Web.Server.Rendezvous;

/// <summary>
/// Serves one TCP connection: greets the device and runs the request it makes. Everything a device sends is
/// untrusted: sizes, formats and signatures are checked, and every wait has a limit.
/// </summary>
internal sealed class ConnectionHandler
{
    /// <summary>
    /// The options.
    /// </summary>
    private readonly RendezvousServerOptions _options;

    /// <summary>
    /// The rooms.
    /// </summary>
    private readonly RoomRegistry _rooms;

    /// <summary>
    /// The waiting meetings.
    /// </summary>
    private readonly MeetingBroker _meetings;

    /// <summary>
    /// The counters.
    /// </summary>
    private readonly ServerStatistics _statistics;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<ConnectionHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionHandler"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="rooms">The rooms.</param>
    /// <param name="meetings">The waiting meetings.</param>
    /// <param name="statistics">The counters.</param>
    /// <param name="logger">The logger.</param>
    public ConnectionHandler(RendezvousServerOptions options, RoomRegistry rooms, MeetingBroker meetings, ServerStatistics statistics, ILogger<ConnectionHandler> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _rooms = rooms ?? throw new ArgumentNullException(nameof(rooms));
        _meetings = meetings ?? throw new ArgumentNullException(nameof(meetings));
        _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Serves a connection until it ends.
    /// </summary>
    /// <param name="socket">The accepted socket; ownership is transferred.</param>
    /// <param name="cancellationToken">Stops the server.</param>
    /// <returns>A task that completes when the connection is closed.</returns>
    public async Task HandleAsync(Socket socket, CancellationToken cancellationToken)
    {
        var observed = RendezvousProtocol.Normalize((IPEndPoint)socket.RemoteEndPoint!);
        await using var channel = new RendezvousChannel(new NetworkStream(socket, ownsSocket: true));
        try
        {
            var nonce = RendezvousProtocol.CreateRandomId();
            RendezvousMessage? first;
            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                limit.CancelAfter(_options.FirstMessageTimeout);
                await channel.SendAsync(new ServerHelloMessage(RendezvousProtocol.Version, nonce), limit.Token).ConfigureAwait(false);
                first = await channel.ReceiveAsync(limit.Token).ConfigureAwait(false);
            }

            switch (first)
            {
                case RegisterMessage register:
                    await HostAsync(channel, observed, nonce, register, cancellationToken).ConfigureAwait(false);
                    break;
                case ConnectMessage connect:
                    await ParticipantAsync(channel, observed, connect, cancellationToken).ConfigureAwait(false);
                    break;
                case AcceptMessage accept:
                    await AcceptAsync(channel, observed, accept, cancellationToken).ConfigureAwait(false);
                    break;
                case null:
                    break;
                default:
                    await RefuseAsync(channel, RendezvousError.BadRequest, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        catch (InvalidDataException ex)
        {
            _logger.LogDebug("{Remote} sent an invalid message: {Error}", observed, ex.Message);
            await RefuseAsync(channel, RendezvousError.BadRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            _logger.LogDebug("The connection of {Remote} ended: {Error}", observed, ex.Message);
        }
    }

    /// <summary>
    /// Registers the room of a host and keeps it while the host pings.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="observed">The address of the host.</param>
    /// <param name="nonce">The nonce the host had to sign.</param>
    /// <param name="register">The request.</param>
    /// <param name="cancellationToken">Stops the server.</param>
    /// <returns>A task that completes when the registration ends.</returns>
    private async Task HostAsync(RendezvousChannel channel, IPEndPoint observed, string nonce, RegisterMessage register, CancellationToken cancellationToken)
    {
        if (!RendezvousPoint.IsValidRoom(register.Room)
            || !RendezvousSignature.TryDecode(register.Certificate, RendezvousSignature.MaxCertificateSize, out var certificate)
            || !RendezvousSignature.TryDecode(register.Signature, RendezvousSignature.MaxSignatureSize, out var signature))
        {
            await RefuseAsync(channel, RendezvousError.BadRequest, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!RendezvousSignature.TryVerify(certificate, RendezvousSignature.BuildPayload(nonce, register.Room), signature, out var owner))
        {
            _logger.LogInformation("{Remote} presented a bad signature", observed);
            await RefuseAsync(channel, RendezvousError.BadSignature, cancellationToken).ConfigureAwait(false);
            return;
        }

        using var link = new HostLink(register.Room, owner, observed, channel);
        switch (_rooms.Claim(link, out var replaced))
        {
            case RoomClaim.Taken:
                await RefuseAsync(channel, RendezvousError.RoomTaken, cancellationToken).ConfigureAwait(false);
                return;
            case RoomClaim.Full:
                _statistics.Refused();
                await RefuseAsync(channel, RendezvousError.ServerFull, cancellationToken).ConfigureAwait(false);
                return;
        }

        replaced?.Close();
        _logger.LogInformation("Host {Owner} at {Remote} holds a room", owner.ToShortString(), observed);
        try
        {
            await channel.SendAsync(new RegisteredMessage(RendezvousProtocol.Format(observed)), cancellationToken).ConfigureAwait(false);
            while (true)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, link.Closed);
                idle.CancelAfter(_options.IdleTimeout);
                RendezvousMessage? message;
                try
                {
                    message = await channel.ReceiveAsync(idle.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Silent for too long, or the same host registered again over a new connection.
                    return;
                }

                switch (message)
                {
                    case null:
                        return;
                    case PingMessage:
                        await channel.SendAsync(new PongMessage(), cancellationToken).ConfigureAwait(false);
                        break;
                    default:
                        await RefuseAsync(channel, RendezvousError.BadRequest, cancellationToken).ConfigureAwait(false);
                        return;
                }
            }
        }
        finally
        {
            _rooms.Release(link);
            link.Close();
            _logger.LogInformation("Host {Owner} at {Remote} left its room", owner.ToShortString(), observed);
        }
    }

    /// <summary>
    /// Announces a participant to the host of a room and, when the host answers, tells the participant where the host is.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="observed">The public address of the participant's punching port.</param>
    /// <param name="connect">The request.</param>
    /// <param name="cancellationToken">Stops the server.</param>
    /// <returns>A task that completes when the participant got its answer.</returns>
    private async Task ParticipantAsync(RendezvousChannel channel, IPEndPoint observed, ConnectMessage connect, CancellationToken cancellationToken)
    {
        if (!RendezvousPoint.IsValidRoom(connect.Room) || observed.AddressFamily != AddressFamily.InterNetwork)
        {
            await RefuseAsync(channel, RendezvousError.BadRequest, cancellationToken).ConfigureAwait(false);
            return;
        }

        _statistics.MeetingRequested();
        if (_rooms.Find(connect.Room) is not { } host)
        {
            await RefuseAsync(channel, RendezvousError.RoomNotFound, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_meetings.Open(host, observed, RendezvousProtocol.ParseLocals(connect.Locals)) is not { } meeting)
        {
            _statistics.Refused();
            await RefuseAsync(channel, RendezvousError.Busy, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            (IPEndPoint Observed, IReadOnlyList<IPEndPoint> Locals) answer;
            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, host.Closed))
            {
                wait.CancelAfter(_options.AnswerTimeout);
                try
                {
                    await host.SendAsync(new IncomingMessage(meeting.Id), wait.Token).ConfigureAwait(false);
                    answer = await meeting.Answer.Task.WaitAsync(wait.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException && !cancellationToken.IsCancellationRequested)
                {
                    var error = host.Closed.IsCancellationRequested || ex is not OperationCanceledException ? RendezvousError.RoomNotFound : RendezvousError.Timeout;
                    await RefuseAsync(channel, error, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            var sameRouter = answer.Observed.Address.Equals(observed.Address);
            await channel.SendAsync(new PunchMessage(RendezvousProtocol.Format(answer.Observed), sameRouter ? Format(answer.Locals) : []), cancellationToken).ConfigureAwait(false);

            // The connection stays open until the participant closes it: it keeps the router mapping of its port.
            using var linger = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linger.CancelAfter(_options.AnswerTimeout);
            await channel.ReceiveAsync(linger.Token).ConfigureAwait(false);
        }
        finally
        {
            _meetings.Close(meeting);
        }
    }

    /// <summary>
    /// Takes the host's answer to a meeting and tells both sides where the other one is.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="observed">The public address of the host's punching port.</param>
    /// <param name="accept">The answer.</param>
    /// <param name="cancellationToken">Stops the server.</param>
    /// <returns>A task that completes when the host got the participant's address.</returns>
    private async Task AcceptAsync(RendezvousChannel channel, IPEndPoint observed, AcceptMessage accept, CancellationToken cancellationToken)
    {
        if (!RendezvousProtocol.IsValidRandomId(accept.Punch) || observed.AddressFamily != AddressFamily.InterNetwork)
        {
            await RefuseAsync(channel, RendezvousError.BadRequest, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_meetings.Take(accept.Punch) is not { } meeting)
        {
            // Unknown, answered already, or the participant gave up.
            await RefuseAsync(channel, RendezvousError.Timeout, cancellationToken).ConfigureAwait(false);
            return;
        }

        var locals = RendezvousProtocol.ParseLocals(accept.Locals);
        meeting.Answer.TrySetResult((observed, locals));
        _statistics.MeetingIntroduced();
        var sameRouter = meeting.Participant.Address.Equals(observed.Address);
        await channel.SendAsync(new PunchMessage(RendezvousProtocol.Format(meeting.Participant), sameRouter ? Format(meeting.ParticipantLocals) : []), cancellationToken).ConfigureAwait(false);

        using var linger = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linger.CancelAfter(_options.AnswerTimeout);
        await channel.ReceiveAsync(linger.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a refusal; the connection is closed after it.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="error">The reason.</param>
    /// <param name="cancellationToken">Stops the server.</param>
    /// <returns>A task that completes when the refusal is sent or could not be.</returns>
    private static async Task RefuseAsync(RendezvousChannel channel, RendezvousError error, CancellationToken cancellationToken)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(TimeSpan.FromSeconds(5));
            await channel.SendAsync(new ErrorMessage(error), limit.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The device has gone; nothing to tell.
        }
    }

    /// <summary>
    /// Formats addresses for a message.
    /// </summary>
    /// <param name="endpoints">The addresses.</param>
    /// <returns>The texts.</returns>
    private static string[] Format(IReadOnlyList<IPEndPoint> endpoints) => [.. endpoints.Select(RendezvousProtocol.Format)];
}
