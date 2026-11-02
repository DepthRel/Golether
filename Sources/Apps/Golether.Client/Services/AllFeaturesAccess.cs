using Golether.Client.Enums;

namespace Golether.Client.Services;

/// <summary>
/// Every feature is available: the access of the desktop application, which has nothing to buy.
/// </summary>
public sealed class AllFeaturesAccess : IFeatureAccess
{
    /// <inheritdoc />
    /// <remarks>Never raised: the set of features does not change.</remarks>
    public event EventHandler? Changed
    {
        add
        {
        }

        remove
        {
        }
    }

    /// <inheritdoc />
    public bool IsAvailable(PaidFeature feature) => true;
}
