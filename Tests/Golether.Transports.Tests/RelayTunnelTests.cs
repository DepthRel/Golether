using System.Net;
using System.Net.Sockets;
using Golether.Transports.Relay;

namespace Golether.Transports.Tests;

/// <summary>
/// Tests of the local entrance through which the cameras and voices of a participant reach the relay of the host.
/// </summary>
public sealed class RelayTunnelTests
{
    /// <summary>
    /// Every connection to the local port gets its own stream to the host, and bytes pass in both directions.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Tunnel_CarriesBytesBothWays()
    {
        var token = TestContext.Current.CancellationToken;
        using var far = new TcpListener(IPAddress.Loopback, 0);
        far.Start();
        var opened = 0;
        await using var tunnel = new RelayTunnel(async cancellationToken =>
        {
            Interlocked.Increment(ref opened);
            return await ConnectAsync(far, cancellationToken);
        });
        tunnel.Start();

        using var near = new TcpClient();
        await near.ConnectAsync(IPAddress.Loopback, tunnel.Port, token);
        using var host = await far.AcceptTcpClientAsync(token);

        await near.GetStream().WriteAsync(new byte[] { 1, 2, 3 }, token);
        var atHost = new byte[3];
        await host.GetStream().ReadExactlyAsync(atHost, token);
        await host.GetStream().WriteAsync(new byte[] { 4, 5 }, token);
        var atNear = new byte[2];
        await near.GetStream().ReadExactlyAsync(atNear, token);

        Assert.Equal([1, 2, 3], atHost);
        Assert.Equal([4, 5], atNear);
        Assert.Equal(1, opened);
    }

    /// <summary>
    /// When the local end closes, the stream to the host is closed too: a relay that lost its client must not linger.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Tunnel_ClosesTheStreamToTheHost()
    {
        var token = TestContext.Current.CancellationToken;
        using var far = new TcpListener(IPAddress.Loopback, 0);
        far.Start();
        await using var tunnel = new RelayTunnel(cancellationToken => ConnectAsync(far, cancellationToken));
        tunnel.Start();
        var near = new TcpClient();
        await near.ConnectAsync(IPAddress.Loopback, tunnel.Port, token);
        using var host = await far.AcceptTcpClientAsync(token);

        near.Dispose();

        Assert.Equal(0, await ReadUntilClosedAsync(host, token));
    }

    /// <summary>
    /// When the host cannot be reached, the local connection is closed instead of hanging.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Tunnel_ClosesLocalConnectionWhenHostIsUnreachable()
    {
        var token = TestContext.Current.CancellationToken;
        await using var tunnel = new RelayTunnel(_ => Task.FromException<Stream>(new IOException("no route")));
        tunnel.Start();
        using var near = new TcpClient();
        await near.ConnectAsync(IPAddress.Loopback, tunnel.Port, token);

        Assert.Equal(0, await ReadUntilClosedAsync(near, token));
    }

    /// <summary>
    /// Opens a connection to a listener, as the session opens a stream to the host.
    /// </summary>
    /// <param name="listener">The far end.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stream.</returns>
    private static async Task<Stream> ConnectAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, cancellationToken);
        return tcp.GetStream();
    }

    /// <summary>
    /// Waits until the other side closes a connection.
    /// </summary>
    /// <param name="client">The connection.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of bytes read before the end: zero when the connection was closed or reset.</returns>
    private static async Task<int> ReadUntilClosedAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            return await client.GetStream().ReadAsync(new byte[1], limit.Token);
        }
        catch (IOException)
        {
            // A reset is a closed connection as well.
            return 0;
        }
    }
}
