using System.Net;
using System.Net.Sockets;
using Golether.Core.Data;
using Golether.Core.Data.Migrations.SQLite.Runner;
using Golether.Core.Data.Stores;
using Golether.Security.Identity;
using Golether.Security.Secrets;
using Golether.Tunnels.AmneziaWG.Configuration;
using Golether.Transports.Relay;
using Golether.Tunnels.AmneziaWG.Control;
using Golether.Tunnels.AmneziaWG.Packages;
using Golether.UI.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of <see cref="TunnelWorkflow"/> with real SQLite stores on both sides.
/// </summary>
public sealed class TunnelWorkflowTests : IDisposable
{
    /// <summary>
    /// The temporary directory with both databases.
    /// </summary>
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("golether-ui-");

    /// <summary>
    /// The host device.
    /// </summary>
    private readonly DeviceIdentity _hostIdentity = DeviceIdentity.CreateNew(TimeProvider.System);

    /// <summary>
    /// The participant device.
    /// </summary>
    private readonly DeviceIdentity _guestIdentity = DeviceIdentity.CreateNew(TimeProvider.System);

    /// <summary>
    /// The tunnel controller of the host.
    /// </summary>
    private readonly ITunnelController _hostController = Substitute.For<ITunnelController>();

    /// <inheritdoc />
    public void Dispose()
    {
        _hostIdentity.Dispose();
        _guestIdentity.Dispose();
        SqliteConnection.ClearAllPools();
        _directory.Delete(recursive: true);
    }

    /// <summary>
    /// The host and a participant exchange packages; the host configuration accumulates participants and survives a
    /// restart (it is read from the protected database records).
    /// </summary>
    [Fact]
    public async Task OfferAnswer_BuildsPersistentHostConfiguration()
    {
        var token = TestContext.Current.CancellationToken;
        var host = CreateWorkflow("host", _hostIdentity, _hostController);
        var guest = CreateWorkflow("guest", _guestIdentity, Substitute.For<ITunnelController>());

        var offer = await host.CreateOfferAsync("Вы", [], token);
        var accepted = await guest.AcceptOfferAsync(offer, "Алексей", [], 45000, token);
        var completed = await host.CompleteOfferAsync(accepted.AnswerText, token);

        Assert.Equal("Алексей", completed.ParticipantName);
        Assert.Equal(accepted.VerificationCode.ToString(), completed.VerificationCode.ToString());
        Assert.Single(completed.Configuration.Peers);
        Assert.Equal(accepted.Configuration.Peers[0].PresharedKey, completed.Configuration.Peers[0].PresharedKey);
        Assert.StartsWith("glt-", accepted.InterfaceName, StringComparison.Ordinal);

        var second = await host.CreateOfferAsync("Вы", [], token);
        var restarted = CreateWorkflow("host", _hostIdentity, _hostController);
        var configuration = await restarted.BuildHostConfigurationAsync(token);
        Assert.Equal(completed.Configuration.Interface, configuration.Interface);
        Assert.Single(configuration.Peers);
        Assert.NotEqual(offer, second);

        await restarted.ApplyAsync(TunnelWorkflow.HostInterfaceName, configuration, token);
        await _hostController.Received(1).UpAsync(TunnelWorkflow.HostInterfaceName, configuration, token);
    }

    /// <summary>
    /// An answer cannot be used twice, and an offer cannot be accepted twice.
    /// </summary>
    [Fact]
    public async Task Packages_AreSingleUse()
    {
        var token = TestContext.Current.CancellationToken;
        var host = CreateWorkflow("host", _hostIdentity, _hostController);
        var guest = CreateWorkflow("guest", _guestIdentity, Substitute.For<ITunnelController>());
        var offer = await host.CreateOfferAsync("Вы", [], token);
        var accepted = await guest.AcceptOfferAsync(offer, "Алексей", [], 45000, token);
        await host.CompleteOfferAsync(accepted.AnswerText, token);

        await Assert.ThrowsAsync<FormatException>(() => host.CompleteOfferAsync(accepted.AnswerText, token));
        await Assert.ThrowsAsync<FormatException>(() => guest.AcceptOfferAsync(offer, "Алексей", [], 45000, token));
    }

    /// <summary>
    /// Exported files are valid configurations.
    /// </summary>
    [Fact]
    public async Task Export_WritesParsableFile()
    {
        var token = TestContext.Current.CancellationToken;
        var host = CreateWorkflow("host", _hostIdentity, _hostController);
        var configuration = await host.BuildHostConfigurationAsync(token);
        var path = Path.Combine(_directory.FullName, "golether0.conf");

        await host.ExportAsync(configuration, path, token);

        Assert.Equal(configuration.Interface, AwgConfiguration.Parse(await File.ReadAllTextAsync(path, token)).Interface);
    }

    /// <summary>
    /// The address the routers of the world see goes into the offer by itself, so a participant from another network
    /// has something to aim at without anybody forwarding a port by hand.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Offer_CarriesThePublicAddress()
    {
        var token = TestContext.Current.CancellationToken;
        var seen = new IPEndPoint(IPAddress.Parse("203.0.113.9"), 9000);
        await using var first = new StunResponder(seen);
        await using var second = new StunResponder(seen);
        var host = CreateWorkflow("public-host", _hostIdentity, _hostController, [first.Address, second.Address]);

        var offer = await host.CreateOfferAsync("Вы", [], token);

        var body = TunnelPackageCodec.Decode<TunnelOfferBody>(TunnelPackageCodec.OfferKind, offer, b => b.HostCertificate).Body;
        Assert.True(
            body.HostEndpoints.Any(e => e.StartsWith("203.0.113.9:", StringComparison.Ordinal)),
            $"Внешнего адреса нет среди: {string.Join(", ", body.HostEndpoints)}");
    }

    /// <summary>
    /// A router that hands out a different port per destination makes the address useless, so it is left out of the
    /// package instead of sending the other side to a port nobody listens on.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Offer_LeavesOutAnUnpredictableAddress()
    {
        var token = TestContext.Current.CancellationToken;
        await using var first = new StunResponder(new IPEndPoint(IPAddress.Parse("203.0.113.9"), 9000));
        await using var second = new StunResponder(new IPEndPoint(IPAddress.Parse("203.0.113.9"), 9001));
        var host = CreateWorkflow("symmetric-host", _hostIdentity, _hostController, [first.Address, second.Address]);

        var offer = await host.CreateOfferAsync("Вы", [], token);

        var body = TunnelPackageCodec.Decode<TunnelOfferBody>(TunnelPackageCodec.OfferKind, offer, b => b.HostCertificate).Body;
        Assert.DoesNotContain(body.HostEndpoints, e => e.StartsWith("203.0.113.9:", StringComparison.Ordinal));
    }

    /// <summary>
    /// A tunnel Golether raised lives as long as Golether does: it is noted in a file, brought down at the end, and
    /// a run that ended badly leaves the note behind so the next run takes care of it.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task RaisedTunnels_AreBroughtDownAtTheEnd()
    {
        var token = TestContext.Current.CancellationToken;
        var state = Path.Combine(_directory.FullName, "raised.txt");
        var host = CreateWorkflow("lifetime", _hostIdentity, _hostController, raisedStatePath: state);
        var configuration = await host.BuildHostConfigurationAsync(token);
        Assert.Empty(host.GetRaised());

        await host.ApplyAsync(TunnelWorkflow.HostInterfaceName, configuration, token);
        Assert.Equal([TunnelWorkflow.HostInterfaceName], host.GetRaised());

        // A new run of the application sees the tunnel of the previous one and brings it down.
        var afterRestart = CreateWorkflow("lifetime", _hostIdentity, _hostController, raisedStatePath: state);
        Assert.Equal([TunnelWorkflow.HostInterfaceName], afterRestart.GetRaised());

        Assert.Empty(await afterRestart.DropRaisedAsync(token));
        await _hostController.Received(1).DownAsync(TunnelWorkflow.HostInterfaceName, token);
        Assert.Empty(afterRestart.GetRaised());

        // Nothing is up any more: closing again asks the tools for nothing.
        _hostController.ClearReceivedCalls();
        Assert.Empty(await afterRestart.DropRaisedAsync(token));
        await _hostController.DidNotReceiveWithAnyArgs().DownAsync(default!, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// When the user refuses the administrator prompt the tunnel stays up and keeps its note, so the next attempt
    /// can offer again instead of forgetting about it.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task RefusedDrop_KeepsTheTunnelNoted()
    {
        var token = TestContext.Current.CancellationToken;
        var state = Path.Combine(_directory.FullName, "refused.txt");
        var controller = Substitute.For<ITunnelController>();
        controller.DownAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new TunnelControlException("Пользователь отклонил запрос.")));
        var host = CreateWorkflow("refused", _hostIdentity, controller, raisedStatePath: state);
        await host.ApplyAsync(TunnelWorkflow.HostInterfaceName, await host.BuildHostConfigurationAsync(token), token);

        var remaining = await host.DropRaisedAsync(token);

        Assert.Equal([TunnelWorkflow.HostInterfaceName], remaining);
        Assert.Equal([TunnelWorkflow.HostInterfaceName], host.GetRaised());
    }

    /// <summary>
    /// Creates a workflow over its own migrated database.
    /// </summary>
    /// <param name="name">The database name.</param>
    /// <param name="identity">The device.</param>
    /// <param name="controller">The tunnel controller.</param>
    /// <param name="stunServers">The STUN servers; none by default, so the tests touch no outside server.</param>
    /// <param name="raisedStatePath">The file remembering raised tunnels, or null to keep them in memory.</param>
    /// <returns>The workflow.</returns>
    private TunnelWorkflow CreateWorkflow(string name, DeviceIdentity identity, ITunnelController controller, IReadOnlyList<string>? stunServers = null, string? raisedStatePath = null)
    {
        var connection = $"Data Source={Path.Combine(_directory.FullName, name + ".db")};Pooling=False";
        SqliteDatabaseMigrator.MigrateUp(connection);
        var services = new ServiceCollection().AddSingleton(TimeProvider.System).AddGoletherData(connection).BuildServiceProvider();
        return new TunnelWorkflow(
            identity,
            services.GetRequiredService<ITunnelStore>(),
            new FilePermissionSecretProtector(),
            controller,
            TimeProvider.System,
            stunServers ?? [],
            raisedStatePath);
    }

    /// <summary>
    /// A STUN server that answers every binding request with one fixed address.
    /// </summary>
    private sealed class StunResponder : IAsyncDisposable
    {
        /// <summary>
        /// The socket.
        /// </summary>
        private readonly Socket _socket = NatTraversal.Open(0);

        /// <summary>
        /// Stops the loop.
        /// </summary>
        private readonly CancellationTokenSource _stop = new();

        /// <summary>
        /// The loop.
        /// </summary>
        private readonly Task _loop;

        /// <summary>
        /// Initializes a new instance of the <see cref="StunResponder"/> class.
        /// </summary>
        /// <param name="reply">The address to report.</param>
        public StunResponder(IPEndPoint reply)
        {
            Address = "127.0.0.1:" + ((IPEndPoint)_socket.LocalEndPoint!).Port;
            _loop = RunAsync(reply, _stop.Token);
        }

        /// <summary>
        /// Gets the address of the server as <c>host:port</c>.
        /// </summary>
        public string Address { get; }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }

            _socket.Dispose();
            _stop.Dispose();
        }

        /// <summary>
        /// Answers binding requests until stopped.
        /// </summary>
        /// <param name="reply">The address to report.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the server stops.</returns>
        private async Task RunAsync(IPEndPoint reply, CancellationToken cancellationToken)
        {
            var buffer = new byte[StunMessage.MaxSize];
            while (!cancellationToken.IsCancellationRequested)
            {
                SocketReceiveFromResult received;
                try
                {
                    received = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), cancellationToken);
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    return;
                }

                if (received.ReceivedBytes < Stun.HeaderSize)
                {
                    continue;
                }

                var request = StunMessage.Parse(buffer.AsSpan(0, received.ReceivedBytes));
                var response = new StunMessage(Stun.Binding, Stun.ClassSuccess, request.TransactionId);
                response.AddXorAddress(Stun.AttrXorMappedAddress, reply);
                await _socket.SendToAsync(response.Encode(), SocketFlags.None, received.RemoteEndPoint, cancellationToken);
            }
        }
    }
}
