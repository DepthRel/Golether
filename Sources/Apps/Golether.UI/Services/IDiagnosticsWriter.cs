using Golether.Session;

namespace Golether.UI.Services;

/// <summary>
/// Saves a diagnostic report next to the application data.
/// </summary>
public interface IDiagnosticsWriter
{
    /// <summary>
    /// Builds a report of the current state and saves it.
    /// </summary>
    /// <param name="snapshot">The session, or <see langword="null"/>.</param>
    /// <param name="notes">Lines about the state of the player and the conference.</param>
    /// <returns>The path of the saved file.</returns>
    Task<string> SaveAsync(SessionSnapshot? snapshot, IReadOnlyList<string> notes);
}
