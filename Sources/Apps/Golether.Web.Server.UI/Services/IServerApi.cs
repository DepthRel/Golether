using Golether.Web.Server.Contracts;

namespace Golether.Web.Server.UI.Services;

/// <summary>
/// Reads the server over its local status address.
/// </summary>
public interface IServerApi
{
    /// <summary>
    /// Reads the health report.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The report, or <see langword="null"/> when the server does not answer.</returns>
    Task<HealthReport?> GetHealthAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the events after a number.
    /// </summary>
    /// <param name="after">The number of the last event the monitor has.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The events, or <see langword="null"/> when the server does not answer.</returns>
    Task<JournalPage?> GetJournalAsync(long after, CancellationToken cancellationToken);
}
