namespace Golether.Core.Data.Entities;

/// <summary>
/// A known device of another participant (table <c>Contacts</c>).
/// </summary>
public sealed class ContactEntity
{
    /// <summary>
    /// Gets or sets the device identifier (64 hexadecimal characters).
    /// </summary>
    public string PeerId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the last known name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the user marked the device as trusted.
    /// </summary>
    public bool IsTrusted { get; set; }

    /// <summary>
    /// Gets or sets the last address the device was reached at.
    /// </summary>
    public string? LastEndpoint { get; set; }

    /// <summary>
    /// Gets or sets user notes.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the time the device was first seen.
    /// </summary>
    public DateTimeOffset FirstSeenAt { get; set; }

    /// <summary>
    /// Gets or sets the time the device was last seen.
    /// </summary>
    public DateTimeOffset LastSeenAt { get; set; }
}
