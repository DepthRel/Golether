using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;

namespace Golether.Core.Data.Migrations.Runner;

/// <summary>
/// Provider-agnostic FluentMigrator runner.
/// </summary>
public sealed class DatabaseMigrator
{
    /// <summary>
    /// The database provider.
    /// </summary>
    private readonly IMigrationProvider _provider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseMigrator"/> class.
    /// </summary>
    /// <param name="provider">The database provider.</param>
    public DatabaseMigrator(IMigrationProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>
    /// Applies all pending migrations.
    /// </summary>
    /// <param name="connectionString">The connection string.</param>
    /// <returns>The versions applied by this call, ascending; empty when the database was up to date.</returns>
    /// <exception cref="ArgumentException">The connection string is invalid.</exception>
    public IReadOnlyList<long> MigrateUp(string connectionString)
    {
        using var services = BuildServices(_provider.PrepareConnectionString(connectionString));
        using var scope = services.CreateScope();
        var versions = scope.ServiceProvider.GetRequiredService<IVersionLoader>();
        var before = versions.VersionInfo.AppliedMigrations().ToHashSet();

        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();

        versions.LoadVersionInfo();
        return versions.VersionInfo.AppliedMigrations().Where(v => !before.Contains(v)).Order().ToArray();
    }

    /// <summary>
    /// Rolls the database back to a version.
    /// </summary>
    /// <param name="connectionString">The connection string.</param>
    /// <param name="version">The version to keep; later migrations are reverted.</param>
    /// <exception cref="ArgumentException">The connection string is invalid.</exception>
    public void MigrateDown(string connectionString, long version)
    {
        using var services = BuildServices(_provider.PrepareConnectionString(connectionString));
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateDown(version);
    }

    /// <summary>
    /// Lists the applied versions.
    /// </summary>
    /// <param name="connectionString">The connection string.</param>
    /// <returns>The applied versions, ascending.</returns>
    /// <exception cref="ArgumentException">The connection string is invalid.</exception>
    public IReadOnlyList<long> GetAppliedVersions(string connectionString)
    {
        using var services = BuildServices(_provider.PrepareConnectionString(connectionString));
        using var scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IVersionLoader>().VersionInfo.AppliedMigrations().Order().ToArray();
    }

    /// <summary>
    /// Builds the FluentMigrator services for a connection string.
    /// </summary>
    /// <param name="connectionString">The prepared connection string.</param>
    /// <returns>The service provider.</returns>
    private ServiceProvider BuildServices(string connectionString)
        => new ServiceCollection()
            .AddLogging()
            .AddFluentMigratorCore()
            .ConfigureRunner(runner =>
            {
                _provider.ConfigureRunner(runner);
                runner.WithGlobalConnectionString(connectionString)
                    .ScanIn(_provider.MigrationsAssembly).For.All();
            })
            .BuildServiceProvider(validateScopes: false);
}
