using Golether.Components.Catalog;
using Golether.Components.Installation;

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
