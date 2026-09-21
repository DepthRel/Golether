using System.Reflection;
using System.Runtime.InteropServices;
using Golether.Localization;

namespace Golether.Media.Conference.GStreamer.Native;

/// <summary>
/// Loads and initializes GStreamer once per process.
/// </summary>
/// <remarks>
/// On Windows the runtime comes from a Golether bundle (<c>bin</c>, <c>lib/gstreamer-1.0</c>, <c>libexec</c>): the
/// plugin path, scanner and registry are set through environment variables before <c>gst_init</c>, and <c>bin</c> is
/// put first on <c>PATH</c> so plugins find their dependencies. On Linux and macOS the system libraries are used.
/// </remarks>
public static unsafe class GstRuntime
{
    /// <summary>
    /// Guards initialization.
    /// </summary>
    private static readonly Lock Gate = new();

    /// <summary>
    /// Loaded library handles by logical name.
    /// </summary>
    private static readonly Dictionary<string, nint> Handles = new(StringComparer.Ordinal);

    /// <summary>
    /// Whether initialization succeeded.
    /// </summary>
    private static bool _initialized;

    /// <summary>
    /// The initialization error, if any.
    /// </summary>
    private static string? _error;

    /// <summary>
    /// Gets the GStreamer version string after initialization.
    /// </summary>
    public static string? Version { get; private set; }

    /// <summary>
    /// Initializes GStreamer.
    /// </summary>
    /// <param name="bundleRoot">The bundle root on Windows; <see langword="null"/> for system libraries.</param>
    /// <param name="registryFile">The plugin registry cache file.</param>
    /// <param name="error">The reason on failure.</param>
    /// <returns><see langword="true"/> when GStreamer is ready.</returns>
    public static bool TryInitialize(string? bundleRoot, string registryFile, out string? error)
    {
        lock (Gate)
        {
            if (_initialized || _error is not null)
            {
                error = _error;
                return _initialized;
            }

            try
            {
                if (bundleRoot is not null)
                {
                    ConfigureBundle(bundleRoot, registryFile);
                }

                LoadLibraries(bundleRoot);
                NativeLibrary.SetDllImportResolver(typeof(GstRuntime).Assembly, Resolve);

                GError* gerror = null;
                if (Gst.InitCheck(0, 0, &gerror) == 0)
                {
                    _error = Texts.Format("Conference.Error.GStreamerFailed", Gst.TakeError(gerror));
                }
                else
                {
                    Version = Gst.TakeString(Gst.VersionString());
                    _initialized = true;
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                _error = Texts.Format("Conference.Error.GStreamerMissing", ex.Message);
            }

            error = _error;
            return _initialized;
        }
    }

    /// <summary>
    /// Sets the environment of a bundled runtime.
    /// </summary>
    /// <param name="root">The bundle root.</param>
    /// <param name="registryFile">The registry cache file.</param>
    private static void ConfigureBundle(string root, string registryFile)
    {
        var bin = Path.Combine(root, "bin");
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (!path.Split(Path.PathSeparator).Contains(bin, StringComparer.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("PATH", bin + Path.PathSeparator + path);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(registryFile)!);
        Environment.SetEnvironmentVariable("GST_PLUGIN_SYSTEM_PATH_1_0", Path.Combine(root, "lib", "gstreamer-1.0"));
        Environment.SetEnvironmentVariable("GST_PLUGIN_PATH_1_0", string.Empty);
        Environment.SetEnvironmentVariable("GST_PLUGIN_SCANNER_1_0", Path.Combine(root, "libexec", "gstreamer-1.0", "gst-plugin-scanner.exe"));
        Environment.SetEnvironmentVariable("GST_REGISTRY_1_0", registryFile);
    }

    /// <summary>
    /// Loads the libraries behind the logical names.
    /// </summary>
    /// <param name="bundleRoot">The bundle root or <see langword="null"/>.</param>
    private static void LoadLibraries(string? bundleRoot)
    {
        var names = new Dictionary<string, string[]>
        {
            [Gst.GLib] = ["glib-2.0-0.dll", "libglib-2.0.so.0", "libglib-2.0.0.dylib"],
            [Gst.GObject] = ["gobject-2.0-0.dll", "libgobject-2.0.so.0", "libgobject-2.0.0.dylib"],
            [Gst.Core] = ["gstreamer-1.0-0.dll", "libgstreamer-1.0.so.0", "libgstreamer-1.0.0.dylib"],
            [Gst.App] = ["gstapp-1.0-0.dll", "libgstapp-1.0.so.0", "libgstapp-1.0.0.dylib"],
            [Gst.Sdp] = ["gstsdp-1.0-0.dll", "libgstsdp-1.0.so.0", "libgstsdp-1.0.0.dylib"],
            [Gst.WebRtc] = ["gstwebrtc-1.0-0.dll", "libgstwebrtc-1.0.so.0", "libgstwebrtc-1.0.0.dylib"],
        };

        var index = OperatingSystem.IsWindows() ? 0 : OperatingSystem.IsMacOS() ? 2 : 1;
        foreach (var (logical, files) in names)
        {
            var file = files[index];
            var candidates = new List<string>();
            if (bundleRoot is not null)
            {
                candidates.Add(Path.Combine(bundleRoot, "bin", file));
            }

            if (OperatingSystem.IsMacOS())
            {
                candidates.Add(Path.Combine("/opt/homebrew/lib", file));
                candidates.Add(Path.Combine("/usr/local/lib", file));
                candidates.Add(Path.Combine("/Library/Frameworks/GStreamer.framework/Libraries", file));
            }

            candidates.Add(file);
            var handle = candidates.Select(c => NativeLibrary.TryLoad(c, out var h) ? h : 0).FirstOrDefault(h => h != 0);
            if (handle == 0)
            {
                throw new DllNotFoundException(file);
            }

            Handles[logical] = handle;
        }
    }

    /// <summary>
    /// Maps logical library names to the loaded handles.
    /// </summary>
    /// <param name="name">The requested name.</param>
    /// <param name="assembly">The requesting assembly.</param>
    /// <param name="searchPath">The search path.</param>
    /// <returns>The handle or zero.</returns>
    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
        => Handles.GetValueOrDefault(name);
}
