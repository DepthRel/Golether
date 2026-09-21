using Golether.Components.Catalog;
using Golether.Components.Installation;
using Golether.Core.Data.Enums;
using Golether.Localization;
using Golether.Media.Player.Mpv;

namespace Golether.UI.Services;

/// <summary>
/// <see cref="IComponentService"/> over <see cref="ComponentLocator"/> and <see cref="ComponentInstaller"/>.
/// </summary>
public sealed class ComponentService : IComponentService
{
    /// <summary>
    /// Finds components.
    /// </summary>
    private readonly ComponentLocator _locator;

    /// <summary>
    /// Installs components.
    /// </summary>
    private readonly ComponentInstaller _installer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentService"/> class.
    /// </summary>
    /// <param name="locator">Finds components.</param>
    /// <param name="installer">Installs components.</param>
    public ComponentService(ComponentLocator locator, ComponentInstaller installer)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        UseFoundVideoLibrary();
    }

    /// <inheritdoc />
    public event EventHandler<ComponentId>? Installed;

    /// <inheritdoc />
    public ComponentStatus GetStatus(ComponentId id) => _locator.GetStatus(id);

    /// <inheritdoc />
    public async Task InstallAsync(ComponentId id, IProgress<InstallProgress> progress, CancellationToken cancellationToken)
    {
        var package = ComponentCatalog.Find(id)
            ?? throw new ComponentInstallException(Texts.Format("Install.Error.NotAutomatic", ComponentCatalog.Describe(id).Title));
        await _installer.InstallAsync(package, progress, cancellationToken).ConfigureAwait(false);
        if (id == ComponentId.Video)
        {
            UseFoundVideoLibrary();
        }

        Installed?.Invoke(this, id);
    }

    /// <summary>
    /// Points the libmpv loader at the located library.
    /// </summary>
    private void UseFoundVideoLibrary()
    {
        var status = _locator.GetStatus(ComponentId.Video);
        if (status.Source is ComponentSource.Bundled or ComponentSource.Installed)
        {
            MpvLibraryResolver.PreferredPath = status.Path;
        }
    }
}
