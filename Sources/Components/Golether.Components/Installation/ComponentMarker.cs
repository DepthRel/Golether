using Golether.Components.Catalog;

namespace Golether.Components.Installation;

/// <summary>
/// The marker written into an installed component.
/// </summary>
/// <param name="Id">The component.</param>
/// <param name="Version">The version.</param>
/// <param name="RuntimeIdentifier">The platform.</param>
/// <param name="Sha256">The checksum of the installed package.</param>
/// <param name="InstalledAt">The installation time.</param>
public sealed record ComponentMarker(ComponentId Id, string Version, string RuntimeIdentifier, string Sha256, DateTimeOffset InstalledAt)
{
    /// <summary>
    /// The marker file name.
    /// </summary>
    public const string FileName = "golether-component.json";
}
