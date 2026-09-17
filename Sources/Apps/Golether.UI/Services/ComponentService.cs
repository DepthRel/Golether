using Golether.Components.Catalog;
using Golether.Components.Installation;
using Golether.Media.Player.Mpv;

namespace Golether.UI.Services;

/// <summary>
/// Reports and installs the native components for the UI.
/// </summary>
public interface IComponentService
{
    /// <summary>
    /// Raised after a component was installed.
    /// </summary>
    event EventHandler<ComponentId>? Installed;

    /// <summary>
    /// Returns the state of a component.
    /// </summary>
    /// <param name="id">The component.</param>
    /// <returns>The state.</returns>
    ComponentStatus GetStatus(ComponentId id);

    /// <summary>
    /// Downloads and installs a component.
    /// </summary>
    /// <param name="id">The component.</param>
    /// <param name="progress">Receives progress.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the component is ready.</returns>
    /// <exception cref="ComponentInstallException">The installation failed.</exception>
    Task InstallAsync(ComponentId id, IProgress<InstallProgress> progress, CancellationToken cancellationToken);
}

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
            ?? throw new ComponentInstallException($"{ComponentCatalog.Describe(id).Title} не устанавливается автоматически на этой системе.");
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
