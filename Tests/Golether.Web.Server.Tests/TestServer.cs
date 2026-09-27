using Golether.Core.Networking;
using Golether.Web.Server.Infrastructure;
using Golether.Web.Server.Options;
using Golether.Web.Server.Rendezvous;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.Web.Server.Tests;

/// <summary>
/// A rendezvous server on a free loopback port, built from the same parts as the real one.
/// </summary>
internal sealed class TestServer : IAsyncDisposable
{
    /// <summary>
    /// The TCP listener.
    /// </summary>
    private readonly RendezvousListener _listener;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestServer"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    private TestServer(RendezvousServerOptions options)
    {
        Options = options;
        Statistics = new ServerStatistics(TimeProvider.System);
        Rooms = new RoomRegistry(options);
        Statistics.CountRooms = () => Rooms.Count;
        var handler = new ConnectionHandler(options, Rooms, new MeetingBroker(options), Statistics, NullLogger<ConnectionHandler>.Instance);
        _listener = new RendezvousListener(options, handler, Statistics, TimeProvider.System, NullLogger<RendezvousListener>.Instance);
    }

    /// <summary>
    /// Gets the options.
    /// </summary>
    public RendezvousServerOptions Options { get; }

    /// <summary>
    /// Gets the rooms.
    /// </summary>
    public RoomRegistry Rooms { get; }

    /// <summary>
    /// Gets the counters.
    /// </summary>
    public ServerStatistics Statistics { get; }

    /// <summary>
    /// Gets the address devices use.
    /// </summary>
    public PeerEndpoint Endpoint => new("127.0.0.1", _listener.LocalEndpoint!.Port);

    /// <summary>
    /// Starts a server on 127.0.0.1 and a free port.
    /// </summary>
    /// <param name="options">Options to change, or <see langword="null"/> for the defaults.</param>
    /// <returns>The running server.</returns>
    public static async Task<TestServer> StartAsync(RendezvousServerOptions? options = null)
    {
        var server = new TestServer((options ?? new RendezvousServerOptions()) with { ListenAddress = "127.0.0.1", Port = 0 });
        await server._listener.StartAsync(CancellationToken.None);
        return server;
    }

    /// <summary>
    /// Creates a new room on this server.
    /// </summary>
    /// <returns>The server and a random room.</returns>
    public RendezvousPoint NewPoint() => new(Endpoint, RendezvousPoint.CreateRoom());

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _listener.StopAsync(CancellationToken.None);
        _listener.Dispose();
    }
}
