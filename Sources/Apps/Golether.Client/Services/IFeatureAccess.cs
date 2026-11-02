using Golether.Client.Enums;

namespace Golether.Client.Services;

/// <summary>
/// Tells which paid features this copy of the client may use.
/// </summary>
public interface IFeatureAccess
{
    /// <summary>
    /// Raised when the available features changed (a purchase or a refund), on any thread.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Tells whether a feature may be used now.
    /// </summary>
    /// <param name="feature">The feature.</param>
    /// <returns><see langword="true"/> when it is available.</returns>
    bool IsAvailable(PaidFeature feature);
}
