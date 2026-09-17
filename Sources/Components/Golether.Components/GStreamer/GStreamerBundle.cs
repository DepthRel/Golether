using Golether.Components.Pe;

namespace Golether.Components.GStreamer;

/// <summary>
/// The part of a GStreamer installation Golether needs, and how to copy it.
/// </summary>
/// <remarks>
/// Layout of a bundle (same as the GStreamer installation, reduced):
/// <c>bin/*.dll</c>, <c>lib/gstreamer-1.0/&lt;plugins&gt;.dll</c>, <c>libexec/gstreamer-1.0/gst-plugin-scanner.exe</c>.
/// </remarks>
public static class GStreamerBundle
{
    /// <summary>
    /// The oldest GStreamer version with the webrtcbin features Golether relies on.
    /// </summary>
    public static readonly Version MinimumVersion = new(1, 24);

    /// <summary>
    /// The plugins used by the conferencing pipelines.
    /// </summary>
    public static readonly IReadOnlyList<string> Plugins =
    [
        "gstcoreelements", "gstapp", "gstautodetect", "gstplayback", "gsttypefindfunctions",
        "gstvideoconvertscale", "gstvideorate", "gstvideofilter", "gstvideotestsrc",
        "gstaudioconvert", "gstaudioresample", "gstaudiotestsrc", "gstvolume", "gstlevel",
        "gstwebrtc", "gstnice", "gstdtls", "gstsrtp", "gstsctp", "gstrtp", "gstrtpmanager",
        "gstvpx", "gstopus", "gstwebrtcdsp", "gstaudiomixer",
        "gstmediafoundation", "gstwasapi2", "gstdirectsound",
    ];

    /// <summary>
    /// The core libraries Golether calls directly.
    /// </summary>
    public static readonly IReadOnlyList<string> CoreLibraries =
    [
        "gstreamer-1.0-0.dll", "gstbase-1.0-0.dll", "gstapp-1.0-0.dll", "gstvideo-1.0-0.dll", "gstaudio-1.0-0.dll",
        "gstwebrtc-1.0-0.dll", "gstsdp-1.0-0.dll", "gobject-2.0-0.dll", "glib-2.0-0.dll",
    ];

    /// <summary>
    /// The relative path of the plugin directory.
    /// </summary>
    public static readonly string PluginDirectory = Path.Combine("lib", "gstreamer-1.0");

    /// <summary>
    /// The relative path of the plugin scanner.
    /// </summary>
    public static readonly string ScannerPath = Path.Combine("libexec", "gstreamer-1.0", "gst-plugin-scanner.exe");

    /// <summary>
    /// Copies the needed plugins, the core libraries, the plugin scanner and all DLLs they depend on.
    /// </summary>
    /// <param name="installRoot">The GStreamer installation.</param>
    /// <param name="targetRoot">The bundle directory (created).</param>
    /// <returns>The number of bytes copied.</returns>
    /// <exception cref="FileNotFoundException">A required plugin or library is missing in the installation.</exception>
    public static long Copy(string installRoot, string targetRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRoot);
        var binSource = Path.Combine(installRoot, "bin");
        var available = Directory.EnumerateFiles(binSource, "*.dll")
            .ToDictionary(p => Path.GetFileName(p), p => p, StringComparer.OrdinalIgnoreCase);

        var roots = new List<(string Source, string Target)>();
        foreach (var plugin in Plugins)
        {
            var source = Path.Combine(installRoot, PluginDirectory, plugin + ".dll");
            roots.Add((Require(source), Path.Combine(targetRoot, PluginDirectory, plugin + ".dll")));
        }

        roots.Add((Require(Path.Combine(installRoot, ScannerPath)), Path.Combine(targetRoot, ScannerPath)));
        foreach (var library in CoreLibraries)
        {
            roots.Add((Require(Path.Combine(binSource, library)), Path.Combine(targetRoot, "bin", library)));
        }

        var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();
        long bytes = 0;
        foreach (var (source, target) in roots)
        {
            bytes += CopyFile(source, target);
            pending.Enqueue(source);
        }

        while (pending.Count > 0)
        {
            foreach (var dependency in PeImports.Read(pending.Dequeue()))
            {
                // Libraries that are not part of GStreamer come from Windows (kernel32, ucrt, …).
                if (available.TryGetValue(dependency, out var path) && copied.Add(dependency))
                {
                    bytes += CopyFile(path, Path.Combine(targetRoot, "bin", Path.GetFileName(path)));
                    pending.Enqueue(path);
                }
            }
        }

        return bytes;
    }

    /// <summary>
    /// Checks that a bundle has the files Golether needs.
    /// </summary>
    /// <param name="root">The bundle or installation.</param>
    /// <returns><see langword="true"/> when the core library and all plugins are present.</returns>
    public static bool IsComplete(string root)
        => File.Exists(Path.Combine(root, "bin", "gstreamer-1.0-0.dll"))
           && Plugins.All(p => File.Exists(Path.Combine(root, PluginDirectory, p + ".dll")));

    /// <summary>
    /// Throws when a file is missing.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The same path.</returns>
    /// <exception cref="FileNotFoundException">The file is missing.</exception>
    private static string Require(string path)
        => File.Exists(path) ? path : throw new FileNotFoundException($"The GStreamer installation has no '{Path.GetFileName(path)}'.", path);

    /// <summary>
    /// Copies a file, creating the directory.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="target">The target.</param>
    /// <returns>The file size.</returns>
    private static long CopyFile(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
        return new FileInfo(target).Length;
    }
}
