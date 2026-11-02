using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Golether.Security.Identity;

namespace Golether.Client.Services;

/// <summary>
/// The device key of a mobile client: a key that cannot be read is replaced by a new one. A phone loses the key of its
/// keystore when its data is restored on another device; this is not a failure to stop at, the host just sees a new
/// device and confirms it with the verification code.
/// </summary>
public sealed class RecoveringDeviceIdentityStore : IDeviceIdentityStore
{
    /// <summary>
    /// The file store of the key.
    /// </summary>
    private readonly FileDeviceIdentityStore _store;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<RecoveringDeviceIdentityStore> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecoveringDeviceIdentityStore"/> class.
    /// </summary>
    /// <param name="store">The file store of the key.</param>
    /// <param name="logger">The logger.</param>
    public RecoveringDeviceIdentityStore(FileDeviceIdentityStore store, ILogger<RecoveringDeviceIdentityStore>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? NullLogger<RecoveringDeviceIdentityStore>.Instance;
    }

    /// <inheritdoc />
    public DeviceIdentity LoadOrCreate()
    {
        try
        {
            return _store.LoadOrCreate();
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "The device key cannot be read, a new one is created");
            File.Delete(_store.FilePath);

            return _store.LoadOrCreate();
        }
    }
}
