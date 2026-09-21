using Golether.Localization;

namespace Golether.Core.Configuration;

/// <summary>
/// Locations of the Golether data: database, device identity, tunnel configurations, components, logs.
/// </summary>
/// <remarks>
/// Golether is self-contained: everything it stores lives in the <c>data</c> folder of its installation, nothing goes
/// to the user profile. Resolution order:
/// <list type="number">
/// <item>the <see cref="DataDirectoryVariable"/> environment variable (development and tests);</item>
/// <item><c>data</c> next to <see cref="InstallationMarkerFileName"/>, looked up from the application directory
/// upwards (packages keep the application in <c>app</c> or in <c>Golether.app/Contents/MacOS</c>);</item>
/// <item><c>data</c> in the application directory (a build output without the marker).</item>
/// </list>
/// </remarks>
public sealed class AppDataPaths
{
    /// <summary>
    /// The environment variable that overrides the data directory.
    /// </summary>
    public const string DataDirectoryVariable = "GOLETHER_DATA_DIR";

    /// <summary>
    /// The file that marks the root of a packaged installation.
    /// </summary>
    public const string InstallationMarkerFileName = "golether.install";

    /// <summary>
    /// The name of the data folder inside the installation.
    /// </summary>
    public const string DataFolderName = "data";

    /// <summary>
    /// How many directories, starting with the application directory, are searched for the marker.
    /// </summary>
    private const int MarkerSearchLevels = 4;

    /// <summary>
    /// The file used to check that the data directory is writable.
    /// </summary>
    private const string WriteProbeFileName = ".write-test";

    /// <summary>
    /// Initializes a new instance of the <see cref="AppDataPaths"/> class.
    /// </summary>
    /// <param name="root">The data directory.</param>
    /// <exception cref="ArgumentException">The directory is blank.</exception>
    public AppDataPaths(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("The data directory must not be blank.", nameof(root));
        }

        Root = Path.GetFullPath(root);
    }

    /// <summary>
    /// Gets the data directory.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// Gets the SQLite database file.
    /// </summary>
    public string DatabaseFile => Path.Combine(Root, "golether.db");

    /// <summary>
    /// Gets the SQLite connection string of <see cref="DatabaseFile"/>.
    /// </summary>
    public string ConnectionString => $"Data Source={DatabaseFile}";

    /// <summary>
    /// Gets the directory of the device identity (key and certificate).
    /// </summary>
    public string IdentityDirectory => Path.Combine(Root, "identity");

    /// <summary>
    /// Gets the directory of generated AmneziaWG configurations.
    /// </summary>
    public string TunnelsDirectory => Path.Combine(Root, "tunnels");

    /// <summary>
    /// Gets the log directory.
    /// </summary>
    public string LogsDirectory => Path.Combine(Root, "logs");

    /// <summary>
    /// Gets the directory of downloaded components (libmpv, GStreamer).
    /// </summary>
    public string ComponentsDirectory => Path.Combine(Root, "components");

    /// <summary>
    /// Gets the directory of the libmpv configuration.
    /// </summary>
    public string MpvDirectory => Path.Combine(Root, "mpv");

    /// <summary>
    /// Gets the GStreamer plugin registry cache file.
    /// </summary>
    public string GStreamerRegistryFile => Path.Combine(Root, "gstreamer", "registry.bin");

    /// <summary>
    /// Resolves the data directory.
    /// </summary>
    /// <param name="applicationDirectory">The application directory; <see cref="AppContext.BaseDirectory"/> when
    /// <see langword="null"/>.</param>
    /// <param name="getEnvironmentVariable">Reads environment variables; <see cref="Environment.GetEnvironmentVariable(string)"/>
    /// when <see langword="null"/>.</param>
    /// <returns>The paths.</returns>
    public static AppDataPaths Resolve(string? applicationDirectory = null, Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        var overridden = getEnvironmentVariable(DataDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return new AppDataPaths(overridden);
        }

        var root = FindInstallationRoot(applicationDirectory ?? AppContext.BaseDirectory);
        return new AppDataPaths(Path.Combine(root, DataFolderName));
    }

    /// <summary>
    /// Finds the installation root: the nearest directory with <see cref="InstallationMarkerFileName"/> among the
    /// application directory and its parents, or the application directory itself.
    /// </summary>
    /// <param name="applicationDirectory">The application directory.</param>
    /// <returns>The installation root.</returns>
    public static string FindInstallationRoot(string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        var appDir = new DirectoryInfo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(applicationDirectory)));
        var current = appDir;
        for (var level = 0; level < MarkerSearchLevels && current is not null; level++, current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, InstallationMarkerFileName)))
            {
                return current.FullName;
            }
        }

        return appDir.FullName;
    }

    /// <summary>
    /// Returns the per-user directory where earlier versions kept the data.
    /// </summary>
    /// <returns>The directory; it may not exist.</returns>
    public static string GetLegacyUserDirectory()
    {
        var baseDir = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "Golether");
    }

    /// <summary>
    /// Moves the data of an earlier version from <paramref name="legacyDirectory"/> into <see cref="Root"/> once, so
    /// the device keeps its identity and contacts.
    /// </summary>
    /// <param name="legacyDirectory">The old data directory.</param>
    /// <returns><see langword="true"/> when data was moved.</returns>
    /// <remarks>
    /// Nothing happens when this installation already has a database or an identity. The old directory is deleted
    /// only after everything has been copied.
    /// </remarks>
    public bool AdoptLegacyData(string legacyDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyDirectory);
        var legacy = Path.TrimEndingDirectorySeparator(Path.GetFullPath(legacyDirectory));
        if (string.Equals(legacy, Path.TrimEndingDirectorySeparator(Root), StringComparison.OrdinalIgnoreCase)
            || !File.Exists(Path.Combine(legacy, "golether.db"))
            || File.Exists(DatabaseFile)
            || (Directory.Exists(IdentityDirectory) && Directory.EnumerateFileSystemEntries(IdentityDirectory).Any()))
        {
            return false;
        }

        CopyDirectory(legacy, Root);
        Directory.Delete(legacy, recursive: true);
        return true;
    }

    /// <summary>
    /// Creates the data directories and checks that they are writable.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The installation folder is read-only; the message tells the user
    /// what to do.</exception>
    public void EnsureCreated()
    {
        try
        {
            foreach (var directory in new[] { Root, IdentityDirectory, TunnelsDirectory, LogsDirectory })
            {
                Directory.CreateDirectory(directory);
            }

            var probe = Path.Combine(Root, WriteProbeFileName);
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new UnauthorizedAccessException(Texts.Format("Error.DataDirectoryNotWritable", Root), ex);
        }

        RestrictToOwner(IdentityDirectory);
        RestrictToOwner(TunnelsDirectory);
    }

    /// <summary>
    /// Copies a directory tree, keeping files that already exist in the target.
    /// </summary>
    /// <param name="source">The source directory.</param>
    /// <param name="target">The target directory.</param>
    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var destination = Path.Combine(target, Path.GetFileName(file));
            if (!File.Exists(destination))
            {
                File.Copy(file, destination);
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }

    /// <summary>
    /// Restricts a directory to the current user on Unix-like systems (mode 700).
    /// </summary>
    /// <param name="directory">The directory.</param>
    private static void RestrictToOwner(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
