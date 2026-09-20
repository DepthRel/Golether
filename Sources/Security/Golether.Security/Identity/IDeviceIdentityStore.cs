namespace Golether.Security.Identity;

/// <summary>
/// Loads the device identity or creates it on first start.
/// </summary>
public interface IDeviceIdentityStore
{
    /// <summary>
    /// Returns the stored identity or creates and stores a new one.
    /// </summary>
    /// <returns>The identity; the caller owns it.</returns>
    DeviceIdentity LoadOrCreate();
}
