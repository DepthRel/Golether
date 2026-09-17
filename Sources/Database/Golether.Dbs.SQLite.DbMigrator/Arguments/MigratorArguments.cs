using System.Globalization;

namespace Golether.Dbs.SQLite.DbMigrator.Arguments;

/// <summary>
/// Parsed command-line arguments of the migrator.
/// </summary>
public sealed record MigratorArguments
{
    /// <summary>
    /// The usage text.
    /// </summary>
    public const string Usage = """
        Golether SQLite database migrator.

        Usage:
          Golether.Dbs.SQLite.DbMigrator (--database <file> | --connection <connection string>) [--list | --down <version>]

        Options:
          --database <file>        SQLite database file (created when missing).
          --connection <string>    Full SQLite connection string.
          --list                   Only list applied migrations.
          --down <version>         Roll back to the given version (0 removes everything).
          --help                   Show this text.

        Without --list and --down all pending migrations are applied.
        """;

    /// <summary>
    /// Gets the connection string.
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Gets a value indicating whether only applied migrations are listed.
    /// </summary>
    public bool ListOnly { get; init; }

    /// <summary>
    /// Gets the rollback target, or <see langword="null"/>.
    /// </summary>
    public long? DownTo { get; init; }

    /// <summary>
    /// Gets a value indicating whether help was requested.
    /// </summary>
    public bool ShowHelp { get; init; }

    /// <summary>
    /// Gets the parse error, or <see langword="null"/>.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Parses arguments.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <returns>The parsed arguments; <see cref="Error"/> describes invalid input.</returns>
    public static MigratorArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? database = null;
        string? connection = null;
        var list = false;
        long? down = null;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h" or "/?":
                    return new MigratorArguments { ShowHelp = true };
                case "--list":
                    list = true;
                    break;
                case "--database" when i + 1 < args.Count:
                    database = args[++i];
                    break;
                case "--connection" when i + 1 < args.Count:
                    connection = args[++i];
                    break;
                case "--down" when i + 1 < args.Count:
                    if (!long.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out var version))
                    {
                        return new MigratorArguments { Error = "--down expects a non-negative version number." };
                    }

                    down = version;
                    break;
                default:
                    return new MigratorArguments { Error = $"Unknown or incomplete argument '{args[i]}'." };
            }
        }

        if ((database is null) == (connection is null))
        {
            return new MigratorArguments { Error = "Specify exactly one of --database and --connection." };
        }

        if (list && down is not null)
        {
            return new MigratorArguments { Error = "--list and --down cannot be combined." };
        }

        if (database is not null && string.IsNullOrWhiteSpace(database))
        {
            return new MigratorArguments { Error = "--database must not be blank." };
        }

        return new MigratorArguments
        {
            ConnectionString = connection ?? $"Data Source={Path.GetFullPath(database!)}",
            ListOnly = list,
            DownTo = down,
        };
    }
}
