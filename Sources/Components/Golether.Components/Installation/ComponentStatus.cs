using Golether.Components.Catalog;

namespace Golether.Components.Installation;

/// <summary>
/// The state of a component.
/// </summary>
/// <param name="Id">The component.</param>
/// <param name="Source">Where it was found.</param>
/// <param name="Path">The library file (video) or runtime root (conference); the library name for system copies.</param>
/// <param name="Package">The package Golether can install, or <see langword="null"/>.</param>
/// <param name="Advice">What the user can do when the component is missing and cannot be installed automatically.</param>
public sealed record ComponentStatus(ComponentId Id, ComponentSource Source, string? Path, ComponentPackage? Package, InstallAdvice? Advice)
{
    /// <summary>
    /// Gets a value indicating whether the component is available.
    /// </summary>
    public bool IsAvailable => Source != ComponentSource.Missing;

    /// <summary>
    /// Gets a value indicating whether Golether can install the component itself.
    /// </summary>
    public bool CanInstall => !IsAvailable && Package is not null;
}
