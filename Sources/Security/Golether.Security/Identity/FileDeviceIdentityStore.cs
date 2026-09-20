using System.Security.Cryptography;
using Golether.Security.Secrets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.Security.Identity;

/// <summary>
/// Stores the device identity as a protected PKCS#12 file (<c>identity/device.key</c>).
/// </summary>
public sealed class FileDeviceIdentityStore : IDeviceIdentityStore
{
    /// <summary>
    /// The identity file name.
    /// </summary>
    public const string FileName = "device.key";

    /// <summary>
    /// The identity directory.
    /// </summary>
    private readonly string _directory;

    /// <summary>
    /// The secret protector.
    /// </summary>
    private readonly ISecretProtector _protector;

    /// <summary>
    /// The time provider for new certificates.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<FileDeviceIdentityStore> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileDeviceIdentityStore"/> class.
    /// </summary>
    /// <param name="directory">The identity directory.</param>
    /// <param name="protector">The secret protector.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public FileDeviceIdentityStore(string directory, ISecretProtector protector, TimeProvider timeProvider, ILogger<FileDeviceIdentityStore>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? NullLogger<FileDeviceIdentityStore>.Instance;
    }

    /// <summary>
    /// Gets the identity file path.
    /// </summary>
    public string FilePath => Path.Combine(_directory, FileName);

    /// <inheritdoc />
    /// <exception cref="CryptographicException">The stored identity cannot be read. The file is left untouched so the
    /// user can restore it; a new identity would change the device identifier for all contacts.</exception>
    public DeviceIdentity LoadOrCreate()
    {
        Directory.CreateDirectory(_directory);
        if (File.Exists(FilePath))
        {
            try
            {
                var identity = DeviceIdentity.FromPkcs12(_protector.Unprotect(File.ReadAllBytes(FilePath)));
                _logger.LogInformation("Loaded device identity {PeerId}", identity.PeerId.ToShortString());
                return identity;
            }
            catch (CryptographicException ex)
            {
                throw new CryptographicException(
                    $"Не удаётся прочитать ключ устройства «{FilePath}» ({_protector.Description}). Обычно так бывает, если папку " +
                    "Golether перенесли на другой компьютер или запустили под другим пользователем. Верните папку на прежнее место " +
                    "или удалите папку identity: будет создан новый ключ, и контактам придётся заново сверить код.",
                    ex);
            }
        }

        var created = DeviceIdentity.CreateNew(_timeProvider);
        SecretProtectors.WriteOwnerOnlyFile(FilePath, _protector.Protect(created.ExportPkcs12()));
        _logger.LogInformation("Created device identity {PeerId} ({Protection})", created.PeerId.ToShortString(), _protector.Description);
        return created;
    }
}
