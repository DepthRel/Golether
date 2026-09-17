using Golether.Core.Data.Entities;
using Golether.Core.Data.Migrations.SQLite.Runner;
using Golether.Core.Data.Stores;
using Golether.Core.Identity;
using Golether.Dbs.SQLite.DbMigrator.Arguments;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Golether.Core.Data.Tests;

/// <summary>
/// Tests of the SQLite migrations, the EF Core stores and the migrator arguments.
/// </summary>
public sealed class DataTests : IDisposable
{
    /// <summary>
    /// The temporary directory of the database.
    /// </summary>
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("golether-db-");

    /// <summary>
    /// The time provider of the stores.
    /// </summary>
    private readonly FakeTimeProvider _time = new(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));

    /// <summary>
    /// Gets the connection string of the test database (nested directory to check that it is created).
    /// </summary>
    private string ConnectionString => $"Data Source={Path.Combine(_directory.FullName, "nested", "golether.db")};Pooling=False";

    /// <inheritdoc />
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _directory.Delete(recursive: true);
    }

    /// <summary>
    /// Migrations are applied once, listed, and rolled back completely.
    /// </summary>
    [Fact]
    public void Migrations_ApplyListAndRollBack()
    {
        Assert.Equal([1L], SqliteDatabaseMigrator.MigrateUp(ConnectionString));
        Assert.Empty(SqliteDatabaseMigrator.MigrateUp(ConnectionString));
        Assert.Equal([1L], SqliteDatabaseMigrator.GetAppliedVersions(ConnectionString));
        Assert.Contains("Contacts", Tables());

        SqliteDatabaseMigrator.MigrateDown(ConnectionString, 0);

        Assert.Empty(SqliteDatabaseMigrator.GetAppliedVersions(ConnectionString));
        Assert.DoesNotContain("Contacts", Tables());
    }

    /// <summary>
    /// Invalid connection strings are reported as argument errors.
    /// </summary>
    [Fact]
    public void Migrations_RejectInvalidConnectionStrings()
    {
        Assert.Throws<ArgumentException>(() => SqliteDatabaseMigrator.MigrateUp(" "));
        Assert.Throws<ArgumentException>(() => SqliteDatabaseMigrator.MigrateUp("Foo=Bar"));
    }

    /// <summary>
    /// Contacts are created, updated, trusted, listed and deleted.
    /// </summary>
    [Fact]
    public async Task Contacts_Lifecycle()
    {
        var stores = CreateStores();
        var token = TestContext.Current.CancellationToken;
        var marina = PeerId.Parse(new string('a', 64));
        var alexey = PeerId.Parse(new string('b', 64));

        await stores.TouchAsync(marina, "Марина", "10.0.0.2:47800", token);
        _time.Advance(TimeSpan.FromMinutes(1));
        await stores.TouchAsync(alexey, "Алексей", null, token);
        _time.Advance(TimeSpan.FromMinutes(1));
        var updated = await stores.TouchAsync(marina, "Марина К.", null, token);

        Assert.Equal("Марина К.", updated.DisplayName);
        Assert.Equal("10.0.0.2:47800", updated.LastEndpoint);
        Assert.Equal(_time.GetUtcNow(), updated.LastSeenAt);
        Assert.Equal(new[] { marina.Value, alexey.Value }, (await stores.ListAsync(token)).Select(c => c.PeerId));

        Assert.True(await stores.SetTrustedAsync(marina, true, token));
        Assert.True((await stores.FindAsync(marina, token))!.IsTrusted);
        Assert.True(await stores.DeleteAsync(alexey, token));
        Assert.False(await stores.DeleteAsync(alexey, token));
        Assert.Null(await stores.FindAsync(alexey, token));
    }

    /// <summary>
    /// Tunnel records and the host interface are stored and read back.
    /// </summary>
    [Fact]
    public async Task Tunnels_AreStored()
    {
        var stores = CreateStores();
        var token = TestContext.Current.CancellationToken;

        await stores.SaveHostInterfaceAsync("golether0", [1, 2, 3], token);
        await stores.SaveHostInterfaceAsync("golether0", [4, 5], token);
        var tunnel = await stores.AddAsync(new TunnelEntity
        {
            OfferId = "offer-1",
            Role = TunnelRole.Host,
            Status = TunnelStatus.Pending,
            InterfaceName = "golether0",
            TunnelAddress = "10.77.41.2",
            SecretData = [9],
            ExpiresAt = _time.GetUtcNow().AddDays(1),
        }, token);
        tunnel.Status = TunnelStatus.Ready;
        tunnel.PeerName = "Алексей";
        await stores.UpdateAsync(tunnel, token);

        Assert.Equal(new byte[] { 4, 5 }, await stores.GetHostInterfaceAsync("golether0", token));
        var found = await stores.FindByOfferAsync(TunnelRole.Host, "offer-1", token);
        Assert.NotNull(found);
        Assert.Equal(TunnelStatus.Ready, found.Status);
        Assert.Equal("Алексей", found.PeerName);
        Assert.Equal(_time.GetUtcNow().AddDays(1), found.ExpiresAt);
        Assert.Null(await stores.FindByOfferAsync(TunnelRole.Participant, "offer-1", token));
        Assert.Single(await stores.ListActiveAsync(token));
    }

    /// <summary>
    /// Settings are inserted and replaced.
    /// </summary>
    [Fact]
    public async Task Settings_AreStored()
    {
        var stores = CreateStores();
        var token = TestContext.Current.CancellationToken;

        Assert.Null(await stores.GetAsync("displayName", token));
        await stores.SetAsync("displayName", "Вы", token);
        await stores.SetAsync("displayName", "Хост", token);

        Assert.Equal("Хост", await stores.GetAsync("displayName", token));
    }

    /// <summary>
    /// The migrator validates its arguments.
    /// </summary>
    [Fact]
    public void MigratorArguments_AreValidated()
    {
        Assert.True(MigratorArguments.Parse(["--help"]).ShowHelp);
        Assert.NotNull(MigratorArguments.Parse([]).Error);
        Assert.NotNull(MigratorArguments.Parse(["--database", "a.db", "--connection", "Data Source=b"]).Error);
        Assert.NotNull(MigratorArguments.Parse(["--database", "a.db", "--list", "--down", "0"]).Error);
        Assert.NotNull(MigratorArguments.Parse(["--database", "a.db", "--down", "x"]).Error);
        Assert.NotNull(MigratorArguments.Parse(["--unknown"]).Error);

        var parsed = MigratorArguments.Parse(["--database", "data/golether.db", "--down", "0"]);
        Assert.Null(parsed.Error);
        Assert.Equal(0, parsed.DownTo);
        Assert.StartsWith("Data Source=", parsed.ConnectionString, StringComparison.Ordinal);
    }

    /// <summary>
    /// The migrator returns documented exit codes.
    /// </summary>
    [Fact]
    public void Migrator_ReturnsExitCodes()
    {
        var file = Path.Combine(_directory.FullName, "cli.db");

        Assert.Equal(0, Dbs.SQLite.DbMigrator.Program.Main(["--database", file]));
        Assert.Equal(0, Dbs.SQLite.DbMigrator.Program.Main(["--database", file, "--list"]));
        Assert.Equal(2, Dbs.SQLite.DbMigrator.Program.Main(["--oops"]));
        SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// Migrates the database and creates the stores through dependency injection.
    /// </summary>
    /// <returns>The stores.</returns>
    private EfStores CreateStores()
    {
        SqliteDatabaseMigrator.MigrateUp(ConnectionString);
        var services = new ServiceCollection()
            .AddSingleton<TimeProvider>(_time)
            .AddGoletherData(ConnectionString)
            .BuildServiceProvider();
        return services.GetRequiredService<EfStores>();
    }

    /// <summary>
    /// Lists the tables of the database.
    /// </summary>
    /// <returns>The table names.</returns>
    private List<string> Tables()
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
