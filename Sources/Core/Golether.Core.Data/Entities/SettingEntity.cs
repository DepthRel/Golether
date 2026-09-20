namespace Golether.Core.Data.Entities;

/// <summary>
/// An application setting (table <c>Settings</c>).
/// </summary>
public sealed class SettingEntity
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the value.
    /// </summary>
    public string Value { get; set; } = string.Empty;
}
