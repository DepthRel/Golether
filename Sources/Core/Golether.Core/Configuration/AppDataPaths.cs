using Golether.Localization;

namespace Golether.Core.Configuration;

/// <summary>
/// Locations of the Golether data: database, device identity, tunnel configurations, components, logs.
/// </summary>
/// <remarks>
/// Golether is self-contained: everything it stores lives in the <c>data</c> folder of its installation, nothing goes
/// to the user profile. The one exception is an installation shared by every user of the computer (it sits in a
/// folder ordinary users cannot write to, such as Program Files): there each user keeps the data in the profile.
/// Resolution order:
/// <list type="number">
/// <item>the <see cref="DATA_DIRECTORY_VARIABLE"/> environment variable (development and tests);</item>
/// <item><c>data</c> next to <see cref="INSTALLATION_MARKER_FILE_NAME"/>, looked up from the application directory
/// upwards (packages keep the application in <c>app</c> or in <c>Golether.app/Contents/MacOS</c>), or the folder
/// of the user when <see cref="SHARED_INSTALLATION_MARKER_FILE_NAME"/> lies next to that marker;</item>
/// <item><c>data</c> in the application directory (a build output without the marker).</item>
/// </list>
/// </remarks>
public sealed class AppDataPaths
{
    /// <summary>
    /// The environment variable that overrides the data directory.
    /// </summary>
    public const string DATA_DIRECTORY_VARIABLE = "GOLETHER_DATA_DIR";

    /// <summary>
    /// The file that marks the root of a packaged installation.
    /// </summary>
    public const string INSTALLATION_MARKER_FILE_NAME = "golether.install";

    /// <summary>
    /// The file an installer puts next to <see cref="INSTALLATION_MARKER_FILE_NAME"/> when the installation serves every
    /// user of the computer: the data then lives in the profile of each user instead of the installation.
    /// </summary>
    public const string SHARED_INSTALLATION_MARKER_FILE_NAME = "golether.shared";

    /// <summary>
    /// The name of the data folder inside the installation.
    /// </summary>
    public const string DATA_FOLDER_NAME = "data";

    /// <summary>
    /// How many directories, starting with the application directory, are searched for the marker.
    /// </summary>
    private const int MARKER_SEARCH_LEVELS = 4;

    /// <summary>
    /// The file used to check that the data directory is writable.
    /// </summary>
    private const string WRITE_PROBE_FILE_NAME = ".write-test";

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
    /// Gets the directory of the chosen avatar shown to other participants when the camera is off.
    /// </summary>
    public string AvatarsDirectory => Path.Combine(Root, "avatars");

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
    /// Gets the directory of the notification sounds written for the players of Linux and macOS, which play files.
    /// </summary>
    public string SoundsDirectory => Path.Combine(Root, "sounds");

    /// <summary>
    /// Resolves the data directory.
    /// </summary>
    /// <param name="applicationDirectory">The application directory; <see cref="AppContext.BaseDirectory"/> when
    /// <see langword="null"/>.</param>
    /// <param name="getEnvironmentVariable">Reads environment variables; <see cref="Environment.GetEnvironmentVariable(string)"/>
    /// when <see langword="null"/>.</param>
    /// <param name="getUserDirectory">Returns the folder of the user for a shared installation;
    /// <see cref="GetUserDirectory"/> when <see langword="null"/>.</param>
    /// <returns>The paths.</returns>
    public static AppDataPaths Resolve(
        string? applicationDirectory = null,
        Func<string, string?>? getEnvironmentVariable = null,
        Func<string>? getUserDirectory = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        var overridden = getEnvironmentVariable(DATA_DIRECTORY_VARIABLE);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return new AppDataPaths(overridden);
        }

        var root = FindInstallationRoot(applicationDirectory ?? AppContext.BaseDirectory);
        return File.Exists(Path.Combine(root, SHARED_INSTALLATION_MARKER_FILE_NAME))
            ? new AppDataPaths((getUserDirectory ?? GetUserDirectory)())
            : new AppDataPaths(Path.Combine(root, DATA_FOLDER_NAME));
    }

    /// <summary>
    /// Finds the installation root: the nearest directory with <see cref="INSTALLATION_MARKER_FILE_NAME"/> among the
    /// application directory and its parents, or the application directory itself.
    /// </summary>
    /// <param name="applicationDirectory">The application directory.</param>
    /// <returns>The installation root.</returns>
    public static string FindInstallationRoot(string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        var appDir = new DirectoryInfo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(applicationDirectory)));
        var current = appDir;
        for (var level = 0; level < MARKER_SEARCH_LEVELS && current is not null; level++, current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, INSTALLATION_MARKER_FILE_NAME)))
            {
                return current.FullName;
            }
        }

        return appDir.FullName;
    }

    /// <summary>
    /// Returns the per-user directory where earlier versions kept the data; the same directory serves a shared
    /// installation, so nothing is moved there.
    /// </summary>
    /// <returns>The directory; it may not exist.</returns>
    public static string GetLegacyUserDirectory() => GetUserDirectory();

    /// <summary>
    /// Returns the directory of Golether in the profile of the current user.
    /// </summary>
    /// <returns>The directory; it may not exist.</returns>
    public static string GetUserDirectory()
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
        if (string.Equals(legacy, Path.TrimEndingDirectorySeparator(Root), StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(Path.Combine(legacy, "golether.db")) ||
            File.Exists(DatabaseFile) ||
            (Directory.Exists(IdentityDirectory) && Directory.EnumerateFileSystemEntries(IdentityDirectory).Any()))
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
            foreach (var directory in new[] { Root, IdentityDirectory, TunnelsDirectory, LogsDirectory, AvatarsDirectory })
            {
                Directory.CreateDirectory(directory);
            }

            var probe = Path.Combine(Root, WRITE_PROBE_FILE_NAME);
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
