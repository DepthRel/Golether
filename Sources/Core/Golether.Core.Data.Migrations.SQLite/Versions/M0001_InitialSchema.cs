using FluentMigrator;
using Golether.Core.Data.Migrations.Base;

namespace Golether.Core.Data.Migrations.SQLite.Versions;

/// <summary>
/// Migration 0001: creates the initial SQLite schema from <c>Scripts/0001_InitialSchema.Up.sql</c>
/// and removes it with <c>Scripts/0001_InitialSchema.Down.sql</c>.
/// </summary>
[Migration(1, "Initial schema")]
public sealed class M0001_InitialSchema : ScriptMigration
{
    /// <inheritdoc />
    protected override string ScriptName => "0001_InitialSchema";
}
