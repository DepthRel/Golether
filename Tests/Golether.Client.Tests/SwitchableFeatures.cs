using Golether.Client.Enums;
using Golether.Client.Services;

namespace Golether.Client.Tests;

/// <summary>
/// Paid features a test opens and closes, as a purchase or a refund would.
/// </summary>
internal sealed class SwitchableFeatures : IFeatureAccess
{
    /// <summary>
    /// The features available now.
    /// </summary>
    private readonly HashSet<PaidFeature> _available = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="SwitchableFeatures"/> class.
    /// </summary>
    /// <param name="available">The features available from the start.</param>
    public SwitchableFeatures(params PaidFeature[] available)
    {
        _available.UnionWith(available);
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public bool IsAvailable(PaidFeature feature) => _available.Contains(feature);

    /// <summary>
    /// Opens or closes a feature and tells the listeners.
    /// </summary>
    /// <param name="feature">The feature.</param>
    /// <param name="available">Whether it becomes available.</param>
    public void Set(PaidFeature feature, bool available)
    {
        if (available)
        {
            _available.Add(feature);
        }
        else
        {
            _available.Remove(feature);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
