namespace Golether.Components.Installation;

/// <summary>
/// Where a component was found.
/// </summary>
public enum ComponentSource
{
    /// <summary>
    /// Not found.
    /// </summary>
    Missing = 0,

    /// <summary>
    /// Shipped with the application (<c>native</c> folder).
    /// </summary>
    Bundled = 1,

    /// <summary>
    /// Installed by Golether into the user data folder.
    /// </summary>
    Installed = 2,

    /// <summary>
    /// Provided by the operating system (distribution packages, Homebrew).
    /// </summary>
    System = 3,
}
