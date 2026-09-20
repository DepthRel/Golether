using Golether.Core.Data.Enums;

namespace Golether.Components.Catalog;

/// <summary>
/// A pinned download of a component for one platform.
/// </summary>
/// <param name="Id">The component.</param>
/// <param name="Version">The upstream version.</param>
/// <param name="RuntimeIdentifier">The .NET runtime identifier, for example <c>win-x64</c>.</param>
/// <param name="Url">The download address (HTTPS).</param>
/// <param name="Sha256">The expected lowercase hexadecimal SHA-256 of the file.</param>
/// <param name="Size">The expected size in bytes.</param>
/// <param name="Format">The package format.</param>
public sealed record ComponentPackage(ComponentId Id, string Version, string RuntimeIdentifier, Uri Url, string Sha256, long Size, PackageFormat Format)
{
    /// <summary>
    /// Gets the file name of the download.
    /// </summary>
    public string FileName => Path.GetFileName(Url.AbsolutePath);
}
