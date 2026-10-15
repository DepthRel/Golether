using System.Net;
using System.Net.Sockets;
using Golether.Core.Enums;

namespace Golether.Transports.Rendezvous.Punching;

/// <summary>
/// Opens a TCP connection between two devices behind routers that let nothing in (TCP hole punching).
/// </summary>
/// <remarks>
/// <para>
/// Both sides talk to the rendezvous server from the port they punch from, so the server sees which public port the
/// router gave to that port. Then both connect to each other from that same port at the same time. The first SYN of
/// each side may be dropped or refused by the other router, but it leaves a mapping in its own: the next SYN of the
/// other side gets in, and the two SYNs cross into one connection (TCP simultaneous open). The attempts repeat until
/// one passes. This works with routers that keep one public port per local port (most home routers and many carrier
/// NATs) and fails with symmetric ones.
/// </para>
/// <para>
/// Several addresses of the other side may connect at once, and the two sides may pick different winners. So only
/// the participant picks (<see cref="ConnectAsync"/>) and speaks first; the host keeps every connection it got
/// (<see cref="AnswerAsync"/>) and takes the one the participant spoke on.
/// </para>
/// </remarks>
public static class TcpPunch
{
    /// <summary>
    /// How long one connection attempt waits before it is dropped and made again.
    /// </summary>
    internal static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(2.5);

    /// <summary>
    /// The pause after a refused attempt, so a refusing router is not flooded.
    /// </summary>
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Creates an IPv4 TCP socket bound to a port that other sockets may share. Every socket of a meeting (the one to
    /// the server and the punching ones) is created this way, as the systems allow a shared port only when all
    /// sockets agree to share it.
    /// </summary>
    /// <param name="port">The local port, or <c>0</c> for any free one.</param>
    /// <returns>The bound socket.</returns>
    public static Socket Bind(int port)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            // On Linux and macOS .NET sets SO_REUSEPORT together with SO_REUSEADDR.
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.NoDelay = true;
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Connects to the other side and returns the first connection that opened (the participant's side).
    /// </summary>
    /// <param name="localPort">The port the server saw this side on.</param>
    /// <param name="targets">The addresses of the other side.</param>
    /// <param name="timeout">How long to try.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The connected socket; the caller owns it.</returns>
    /// <exception cref="RendezvousException">No connection opened in time (<see cref="RendezvousError.PunchFailed"/>).</exception>
    public static Task<Socket> ConnectAsync(int localPort, IReadOnlyList<IPEndPoint> targets, TimeSpan timeout, CancellationToken cancellationToken)
        => RaceAsync(localPort, targets, timeout, static (_, _) => Task.FromResult(true), cancellationToken);

    /// <summary>
    /// Connects to the other side and returns the connection on which the other side spoke first (the host's side).
    /// Connections that close without a byte were dropped by the other side in favour of another one.
    /// </summary>
    /// <param name="localPort">The port the server saw this side on.</param>
    /// <param name="targets">The addresses of the other side.</param>
    /// <param name="timeout">How long to try.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The connected socket with the first bytes of the other side still unread; the caller owns it.</returns>
    /// <exception cref="RendezvousException">No connection opened in time (<see cref="RendezvousError.PunchFailed"/>).</exception>
    public static Task<Socket> AnswerAsync(int localPort, IReadOnlyList<IPEndPoint> targets, TimeSpan timeout, CancellationToken cancellationToken)
        => RaceAsync(localPort, targets, timeout, WaitForDataAsync, cancellationToken);

    /// <summary>
    /// Tries all addresses at once and keeps the first connection the check accepts.
    /// </summary>
    /// <param name="localPort">The local port.</param>
    /// <param name="targets">The addresses of the other side.</param>
    /// <param name="timeout">How long to try.</param>
    /// <param name="accept">Decides whether an open connection is the one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The winning socket.</returns>
    private static async Task<Socket> RaceAsync(
        int localPort,
        IReadOnlyList<IPEndPoint> targets,
        TimeSpan timeout,
        Func<Socket, CancellationToken, Task<bool>> accept,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentOutOfRangeException.ThrowIfLessThan(localPort, 1);
        if (targets.Count == 0)
        {
            throw new RendezvousException(RendezvousError.PunchFailed);
        }

        using var race = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        race.CancelAfter(timeout);
        var attempts = targets.Distinct().Select(target => AttemptAsync(localPort, target, accept, race.Token)).ToList();
        Socket? winner = null;
        while (attempts.Count > 0)
        {
            var done = await Task.WhenAny(attempts).ConfigureAwait(false);
            attempts.Remove(done);
            var socket = await done.ConfigureAwait(false);
            if (socket is null)
            {
                continue;
            }

            if (winner is null)
            {
                winner = socket;
                await race.CancelAsync().ConfigureAwait(false);
            }
            else
            {
                socket.Dispose();
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            // The caller gave up while a connection opened: nobody would close it.
            winner?.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
        }

        return winner ?? throw new RendezvousException(RendezvousError.PunchFailed);
    }

    /// <summary>
    /// Connects to one address again and again until a connection passes the check or the time runs out.
    /// </summary>
    /// <param name="localPort">The local port.</param>
    /// <param name="target">The address.</param>
    /// <param name="accept">Decides whether an open connection is the one.</param>
    /// <param name="cancellationToken">Stops the attempts.</param>
    /// <returns>The connected socket, or <see langword="null"/> when the attempts stopped.</returns>
    private static async Task<Socket?> AttemptAsync(int localPort, IPEndPoint target, Func<Socket, CancellationToken, Task<bool>> accept, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Socket? socket = null;
            try
            {
                socket = Bind(localPort);
                using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    attempt.CancelAfter(AttemptTimeout);
                    await socket.ConnectAsync(target, attempt.Token).ConfigureAwait(false);
                }

                if (await accept(socket, cancellationToken).ConfigureAwait(false))
                {
                    var won = socket;
                    socket = null;
                    return won;
                }
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // Refused, dropped or timed out: the other router has not opened yet, or this address is wrong.
            }
            finally
            {
                socket?.Dispose();
            }

            try
            {
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return null;
    }

    /// <summary>
    /// Waits until the other side sends something, without taking it off the socket.
    /// </summary>
    /// <param name="socket">The connected socket.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when bytes arrived; <see langword="false"/> when the other side closed.</returns>
    private static async Task<bool> WaitForDataAsync(Socket socket, CancellationToken cancellationToken)
    {
        var probe = new byte[1];
        return await socket.ReceiveAsync(probe, SocketFlags.Peek, cancellationToken).ConfigureAwait(false) > 0;
    }
}
