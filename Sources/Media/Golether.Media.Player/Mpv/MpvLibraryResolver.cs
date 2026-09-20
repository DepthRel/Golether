using System.Reflection;
using System.Runtime.InteropServices;

namespace Golether.Media.Player.Mpv;

/// <summary>
/// Locates the libmpv shared library of the current platform.
/// </summary>
public static class MpvLibraryResolver
{
    /// <summary>
    /// The environment variable with an explicit library path.
    /// </summary>
    public const string LibraryPathVariable = "GOLETHER_LIBMPV";

    /// <summary>
    /// Guards registration.
    /// </summary>
    private static readonly Lock Gate = new();

    /// <summary>
    /// The loaded library handle.
    /// </summary>
    private static nint _handle;

    /// <summary>
    /// Whether the resolver has been registered.
    /// </summary>
    private static bool _registered;

    /// <summary>
    /// Gets or sets a library path found by the component locator; it is tried first.
    /// </summary>
    public static string? PreferredPath { get; set; }

    /// <summary>
    /// Gets the file names tried on the current platform.
    /// </summary>
    public static IReadOnlyList<string> CandidateNames
        => OperatingSystem.IsWindows() ? ["libmpv-2.dll", "mpv-2.dll", "mpv-1.dll"]
            : OperatingSystem.IsMacOS() ? ["libmpv.2.dylib", "libmpv.dylib"]
            : ["libmpv.so.2", "libmpv.so"];

    /// <summary>
    /// Loads libmpv.
    /// </summary>
    /// <param name="error">The reason when the library was not found.</param>
    /// <returns><see langword="true"/> when libmpv is available.</returns>
    public static bool TryLoad(out string? error)
    {
        lock (Gate)
        {
            error = null;
            if (_handle != 0)
            {
                return true;
            }

            foreach (var candidate in EnumerateCandidates())
            {
                if (NativeLibrary.TryLoad(candidate, out _handle))
                {
                    break;
                }
            }

            if (_handle == 0)
            {
                error = "Компонент видео (libmpv) не установлен.";
                return false;
            }

            if (!_registered)
            {
                NativeLibrary.SetDllImportResolver(typeof(LibMpv).Assembly, Resolve);
                _registered = true;
            }

            var major = LibMpv.ClientApiVersion() >> 16;
            if (major < 2)
            {
                error = $"Нужна libmpv с client API 2.x, найдена {major}.x.";
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Resolves the logical library name.
    /// </summary>
    /// <param name="name">The requested library name.</param>
    /// <param name="assembly">The requesting assembly.</param>
    /// <param name="searchPath">The search path.</param>
    /// <returns>The handle, or zero for other libraries.</returns>
    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
        => name == LibMpv.LibraryName ? _handle : 0;

    /// <summary>
    /// Lists the paths and names to try.
    /// </summary>
    /// <returns>The candidates in order.</returns>
    private static IEnumerable<string> EnumerateCandidates()
    {
        if (!string.IsNullOrWhiteSpace(PreferredPath))
        {
            yield return PreferredPath;
        }

        var explicitPath = Environment.GetEnvironmentVariable(LibraryPathVariable);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            yield return explicitPath;
        }

        var baseDir = AppContext.BaseDirectory;
        foreach (var name in CandidateNames)
        {
            yield return Path.Combine(baseDir, name);
            yield return Path.Combine(baseDir, "native", name);
        }

        if (OperatingSystem.IsMacOS())
        {
            foreach (var prefix in new[] { "/opt/homebrew/lib", "/usr/local/lib" })
            {
                yield return Path.Combine(prefix, "libmpv.dylib");
            }
        }

        foreach (var name in CandidateNames)
        {
            yield return name;
        }
    }
}
