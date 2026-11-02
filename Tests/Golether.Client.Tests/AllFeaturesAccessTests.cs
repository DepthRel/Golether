using Golether.Client.Enums;
using Golether.Client.Services;

namespace Golether.Client.Tests;

/// <summary>
/// Tests of <see cref="AllFeaturesAccess"/>: the desktop application has no paid features.
/// </summary>
public sealed class AllFeaturesAccessTests
{
    /// <summary>
    /// Every paid feature is available.
    /// </summary>
    [Fact]
    public void EverythingIsAvailable()
    {
        var access = new AllFeaturesAccess();

        Assert.All(Enum.GetValues<PaidFeature>(), feature => Assert.True(access.IsAvailable(feature)));
    }
}
