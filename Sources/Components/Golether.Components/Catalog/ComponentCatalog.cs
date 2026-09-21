using System.Runtime.InteropServices;
using Golether.Core.Data.Enums;
using Golether.Localization;

namespace Golether.Components.Catalog;

/// <summary>
/// The packages Golether can install by itself. Every entry is pinned by size and SHA-256, so a changed or
/// substituted file is rejected.
/// </summary>
/// <remarks>
/// libmpv: <c>shinchiro/mpv-winbuild-cmake</c> release 20260903 (GPL build; hashes match the release digests).
/// GStreamer: official MSVC installers 1.28.7 from gstreamer.freedesktop.org (hashes match the published
/// <c>.sha256sum</c> files). Linux and macOS use the packages of the distribution or Homebrew instead.
/// </remarks>
public static class ComponentCatalog
{
    /// <summary>
    /// The pinned packages.
    /// </summary>
    public static IReadOnlyList<ComponentPackage> Packages { get; } =
    [
        new(ComponentId.Video, "20260903", "win-x64",
            new Uri("https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20260903/mpv-dev-x86_64-20260903-git-69e63f425a.7z"),
            "fac135c68a35b7639e39d72c0c365104edbaebdea39a0dfdd8c36e8c8e80faef", 31_363_218, PackageFormat.SevenZip),
        new(ComponentId.Video, "20260903", "win-arm64",
            new Uri("https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20260903/mpv-dev-aarch64-20260903-git-69e63f425a.7z"),
            "9d4e0cf7370fd1dd9a91a9d8139f24a88ece9e58b00f5a9ca50b391d03114f2f", 25_950_219, PackageFormat.SevenZip),
        new(ComponentId.Conference, "1.28.7", "win-x64",
            new Uri("https://gstreamer.freedesktop.org/data/pkg/windows/1.28.7/msvc/gstreamer-1.0-msvc-x86_64-1.28.7.exe"),
            "032fc6062b8539838fc8da22589cb9b24c5d820baa7f8cc160af9ea08395badf", 526_852_553, PackageFormat.InnoSetup),
        new(ComponentId.Conference, "1.28.7", "win-arm64",
            new Uri("https://gstreamer.freedesktop.org/data/pkg/windows/1.28.7/msvc/gstreamer-1.0-msvc-arm64-1.28.7.exe"),
            "eb8bd2547d52a96c570f82b0c581fa5a8992f672aba7fb9668370536f203d1f8", 314_397_421, PackageFormat.InnoSetup),
        new(ComponentId.Tunnel, "3.1.0", "win-x64",
            new Uri("https://github.com/amnezia-vpn/amneziawg-windows-client/releases/download/3.1.0/amneziawg-amd64-3.1.0.msi"),
            "a1b48ea8699cd347832a3691d832004574ef8ad65bcf887611ac8acb99b7de8b", 3_641_344, PackageFormat.WindowsInstaller),
        new(ComponentId.Tunnel, "3.1.0", "win-arm64",
            new Uri("https://github.com/amnezia-vpn/amneziawg-windows-client/releases/download/3.1.0/amneziawg-arm64-3.1.0.msi"),
            "3aec023884944890fac151f9cd4be3e92d39f9987c49da248837529c006ea2aa", 3_330_048, PackageFormat.WindowsInstaller),
    ];

    /// <summary>
    /// Gets the runtime identifier of the running process (<c>win-x64</c>, <c>linux-arm64</c>, <c>osx-arm64</c>…).
    /// </summary>
    public static string CurrentRuntimeIdentifier
    {
        get
        {
            var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
            var arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                Architecture.X86 => "x86",
                var other => other.ToString().ToLowerInvariant(),
            };
            return $"{os}-{arch}";
        }
    }

    /// <summary>
    /// Finds the package of a component for a platform.
    /// </summary>
    /// <param name="id">The component.</param>
    /// <param name="runtimeIdentifier">The runtime identifier; the current one when <see langword="null"/>.</param>
    /// <returns>The package, or <see langword="null"/> when the component is not installed automatically there.</returns>
    public static ComponentPackage? Find(ComponentId id, string? runtimeIdentifier = null)
    {
        var rid = runtimeIdentifier ?? CurrentRuntimeIdentifier;
        return Packages.FirstOrDefault(p => p.Id == id && string.Equals(p.RuntimeIdentifier, rid, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Describes a component for the user.
    /// </summary>
    /// <param name="id">The component.</param>
    /// <returns>The description.</returns>
    public static ComponentDescription Describe(ComponentId id) => id switch
    {
        ComponentId.Video => new(Texts.Get("Component.Video.Title"), Texts.Get("Component.Video.Purpose"), "GPL-2.0-or-later", "mpv.io"),
        ComponentId.Tunnel => new(Texts.Get("Component.Tunnel.Title"), Texts.Get("Component.Tunnel.Purpose"), "MIT", "github.com/amnezia-vpn"),
        _ => new(Texts.Get("Component.Conference.Title"), Texts.Get("Component.Conference.Purpose"), "LGPL-2.1-or-later", "gstreamer.freedesktop.org"),
    };
}
