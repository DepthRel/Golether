using System.Runtime.InteropServices;
using System.Text.Json;
using Golether.Components.Catalog;
using Golether.Components.GStreamer;

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

/// <summary>
/// Finds components: shipped with the application first, then installed by Golether, then from the system.
/// </summary>
public sealed class ComponentLocator
{
    /// <summary>
    /// The folder with shipped native files.
    /// </summary>
    private readonly string _bundledDirectory;

    /// <summary>
    /// The installation root.
    /// </summary>
    private readonly string _installRoot;

    /// <summary>
    /// Checks whether a system library can be loaded.
    /// </summary>
    private readonly Func<string, bool> _canLoadSystemLibrary;

    /// <summary>
    /// Reads <c>/etc/os-release</c>.
    /// </summary>
    private readonly Func<string?> _readOsRelease;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentLocator"/> class.
    /// </summary>
    /// <param name="bundledDirectory">The folder with shipped native files (<c>&lt;app&gt;/native</c>).</param>
    /// <param name="installRoot">The installation root of <see cref="ComponentInstaller"/>.</param>
    /// <param name="canLoadSystemLibrary">Checks a system library by name; <see cref="NativeLibrary.TryLoad(string, out nint)"/> when <see langword="null"/>.</param>
    /// <param name="readOsRelease">Reads the Linux distribution description; the real file when <see langword="null"/>.</param>
    public ComponentLocator(string bundledDirectory, string installRoot, Func<string, bool>? canLoadSystemLibrary = null, Func<string?>? readOsRelease = null)
    {
        _bundledDirectory = bundledDirectory ?? throw new ArgumentNullException(nameof(bundledDirectory));
        _installRoot = installRoot ?? throw new ArgumentNullException(nameof(installRoot));
        _canLoadSystemLibrary = canLoadSystemLibrary ?? (name => NativeLibrary.TryLoad(name, out _));
        _readOsRelease = readOsRelease ?? (() => File.Exists("/etc/os-release") ? File.ReadAllText("/etc/os-release") : null);
    }

    /// <summary>
    /// Gets the library names of libmpv on the current platform.
    /// </summary>
    public static IReadOnlyList<string> VideoLibraryNames
        => OperatingSystem.IsWindows() ? ["libmpv-2.dll", "mpv-2.dll"]
            : OperatingSystem.IsMacOS() ? ["libmpv.2.dylib", "libmpv.dylib"]
            : ["libmpv.so.2", "libmpv.so"];

    /// <summary>
    /// Gets the library name of GStreamer on the current platform.
    /// </summary>
    public static string ConferenceLibraryName
        => OperatingSystem.IsWindows() ? "gstreamer-1.0-0.dll"
            : OperatingSystem.IsMacOS() ? "libgstreamer-1.0.0.dylib"
            : "libgstreamer-1.0.so.0";

    /// <summary>
    /// Determines the state of a component.
    /// </summary>
    /// <param name="id">The component.</param>
    /// <returns>The state.</returns>
    public ComponentStatus GetStatus(ComponentId id)
    {
        var package = ComponentCatalog.Find(id);
        var (source, path) = id == ComponentId.Video ? FindVideo() : FindConference();
        var advice = source == ComponentSource.Missing && package is null
            ? InstallAdvice.For(id, OperatingSystem.IsMacOS() ? OsFamily.MacOS : OperatingSystem.IsWindows() ? OsFamily.Windows : OsFamily.Linux, _readOsRelease())
            : null;
        return new ComponentStatus(id, source, path, package, advice);
    }

    /// <summary>
    /// Finds libmpv.
    /// </summary>
    /// <returns>The source and the file.</returns>
    private (ComponentSource Source, string? Path) FindVideo()
    {
        foreach (var name in VideoLibraryNames)
        {
            var bundled = Path.Combine(_bundledDirectory, name);
            if (File.Exists(bundled))
            {
                return (ComponentSource.Bundled, bundled);
            }
        }

        if (FindInstalled(ComponentId.Video) is { } installed)
        {
            foreach (var name in VideoLibraryNames)
            {
                var file = Path.Combine(installed, name);
                if (File.Exists(file))
                {
                    return (ComponentSource.Installed, file);
                }
            }
        }

        if (!OperatingSystem.IsWindows())
        {
            foreach (var name in VideoLibraryNames)
            {
                if (_canLoadSystemLibrary(name))
                {
                    return (ComponentSource.System, name);
                }
            }
        }

        return (ComponentSource.Missing, null);
    }

    /// <summary>
    /// Finds the GStreamer runtime.
    /// </summary>
    /// <returns>The source and the runtime root.</returns>
    private (ComponentSource Source, string? Path) FindConference()
    {
        var bundled = Path.Combine(_bundledDirectory, "gstreamer");
        if (GStreamerBundle.IsComplete(bundled))
        {
            return (ComponentSource.Bundled, bundled);
        }

        if (FindInstalled(ComponentId.Conference) is { } installed && GStreamerBundle.IsComplete(Path.Combine(installed, "gstreamer")))
        {
            return (ComponentSource.Installed, Path.Combine(installed, "gstreamer"));
        }

        if (!OperatingSystem.IsWindows() && _canLoadSystemLibrary(ConferenceLibraryName))
        {
            return (ComponentSource.System, ConferenceLibraryName);
        }

        return (ComponentSource.Missing, null);
    }

    /// <summary>
    /// Returns the newest installed version directory of a component with a valid marker.
    /// </summary>
    /// <param name="id">The component.</param>
    /// <returns>The directory, or <see langword="null"/>.</returns>
    private string? FindInstalled(ComponentId id)
    {
        var root = Path.Combine(_installRoot, id.ToString().ToLowerInvariant());
        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory.EnumerateDirectories(root)
            .Select(directory => (Directory: directory, Marker: ReadMarker(directory)))
            .Where(entry => entry.Marker is not null && entry.Marker.Id == id)
            .OrderByDescending(entry => entry.Marker!.InstalledAt)
            .Select(entry => entry.Directory)
            .FirstOrDefault();
    }

    /// <summary>
    /// Reads the marker of an installation.
    /// </summary>
    /// <param name="directory">The installation directory.</param>
    /// <returns>The marker, or <see langword="null"/> when absent or damaged.</returns>
    private static ComponentMarker? ReadMarker(string directory)
    {
        var file = Path.Combine(directory, ComponentMarker.FileName);
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<ComponentMarker>(File.ReadAllBytes(file), ComponentInstaller.MarkerJson) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }
}
