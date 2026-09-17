using FluentMigrator;

namespace Golether.Core.Data.Migrations.Base;

/// <summary>
/// Base class of Golether migrations: the C# class only identifies the migration, the schema change lives in embedded
/// SQL scripts of the provider-specific migrations assembly.
/// </summary>
/// <remarks>
/// For <see cref="ScriptName"/> <c>0001_InitialSchema</c> the migration executes the embedded resources
/// <c>0001_InitialSchema.Up.sql</c> and <c>0001_InitialSchema.Down.sql</c>.
/// </remarks>
public abstract class ScriptMigration : Migration
{
    /// <summary>
    /// Gets the script base name, for example <c>0001_InitialSchema</c>.
    /// </summary>
    protected abstract string ScriptName { get; }

    /// <summary>
    /// Gets the embedded resource name of the upgrade script.
    /// </summary>
    public string UpScript => $"{ScriptName}.Up.sql";

    /// <summary>
    /// Gets the embedded resource name of the downgrade script.
    /// </summary>
    public string DownScript => $"{ScriptName}.Down.sql";

    /// <summary>
    /// Executes the upgrade script.
    /// </summary>
    public override void Up() => Execute.EmbeddedScript(UpScript);

    /// <summary>
    /// Executes the downgrade script.
    /// </summary>
    public override void Down() => Execute.EmbeddedScript(DownScript);
}
