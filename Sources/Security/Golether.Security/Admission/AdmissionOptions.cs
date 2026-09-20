namespace Golether.Security.Admission;

/// <summary>
/// Admission policy of the host.
/// </summary>
public sealed record AdmissionOptions
{
    /// <summary>
    /// Gets a value indicating whether trusted contacts join without a prompt (default <see langword="false"/>).
    /// </summary>
    public bool AutoApproveKnownContacts { get; init; }

    /// <summary>
    /// Gets the time the host has to answer (default 2 minutes).
    /// </summary>
    public TimeSpan PromptTimeout { get; init; } = TimeSpan.FromMinutes(2);
}
