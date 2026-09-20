namespace Golether.Core.Data.Stores;

/// <summary>
/// Access to application settings.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Returns a setting.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a setting.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the value is saved.</returns>
    Task SetAsync(string key, string value, CancellationToken cancellationToken);
}
