using Golether.Core.Data;
using Golether.Core.Data.Migrations.SQLite.Runner;
using Golether.Core.Data.Stores;
using Golether.Security.Identity;
using Golether.Security.Secrets;
using Golether.Tunnels.AmneziaWG.Configuration;
using Golether.Tunnels.AmneziaWG.Control;
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
    /// Creates a workflow over its own migrated database.
    /// </summary>
    /// <param name="name">The database name.</param>
    /// <param name="identity">The device.</param>
    /// <param name="controller">The tunnel controller.</param>
    /// <returns>The workflow.</returns>
    private TunnelWorkflow CreateWorkflow(string name, DeviceIdentity identity, ITunnelController controller)
    {
        var connection = $"Data Source={Path.Combine(_directory.FullName, name + ".db")};Pooling=False";
        SqliteDatabaseMigrator.MigrateUp(connection);
        var services = new ServiceCollection().AddSingleton(TimeProvider.System).AddGoletherData(connection).BuildServiceProvider();
        return new TunnelWorkflow(identity, services.GetRequiredService<ITunnelStore>(), new FilePermissionSecretProtector(), controller, TimeProvider.System);
    }
}
