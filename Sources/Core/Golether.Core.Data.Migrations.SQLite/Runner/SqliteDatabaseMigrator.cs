using Golether.Core.Data.Migrations.Runner;
using Golether.Core.Data.Migrations.SQLite.Provider;

namespace Golether.Core.Data.Migrations.SQLite.Runner;

/// <summary>
/// Entry point for running the Golether SQLite migrations.
/// </summary>
public static class SqliteDatabaseMigrator
{
    /// <summary>
    /// The shared migrator configured for SQLite.
    /// </summary>
    private static readonly DatabaseMigrator Migrator = new(new SqliteMigrationProvider());

    /// <summary>
    /// Applies all pending migrations.
    /// </summary>
    /// <param name="connectionString">The SQLite connection string (<c>Data Source=...</c>).</param>
    /// <returns>The versions applied by this call, ascending.</returns>
    /// <exception cref="ArgumentException">The connection string is invalid.</exception>
    public static IReadOnlyList<long> MigrateUp(string connectionString) => Migrator.MigrateUp(connectionString);

    /// <summary>
    /// Rolls the database back to a version.
    /// </summary>
    /// <param name="connectionString">The SQLite connection string.</param>
    /// <param name="version">The version to keep.</param>
    /// <exception cref="ArgumentException">The connection string is invalid.</exception>
    public static void MigrateDown(string connectionString, long version) => Migrator.MigrateDown(connectionString, version);

    /// <summary>
    /// Lists applied migration versions.
    /// </summary>
    /// <param name="connectionString">The SQLite connection string.</param>
    /// <returns>The applied versions, ascending.</returns>
    /// <exception cref="ArgumentException">The connection string is invalid.</exception>
    public static IReadOnlyList<long> GetAppliedVersions(string connectionString) => Migrator.GetAppliedVersions(connectionString);
}
