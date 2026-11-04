namespace Golether.Components.Installation;

/// <summary>
/// The files Golether keeps from the AmneziaWG package for Windows. They are used only by Golether, from its own
/// data folder: an AmneziaWG the user installed themselves keeps working next to it, with its own tunnels.
/// </summary>
public static class AmneziaWgBundle
{
    /// <summary>
    /// The folder the files are kept in inside the component directory.
    /// </summary>
    public const string DIRECTORY_NAME = "amneziawg";

    /// <summary>
    /// The program that raises and drops tunnels.
    /// </summary>
    public const string EXECUTABLE = "amneziawg.exe";

    /// <summary>
    /// The files taken out of the package: the tunnel program, the command-line tool and the virtual adapter driver.
    /// </summary>
    public static IReadOnlyList<string> Files { get; } = [EXECUTABLE, "awg.exe", "wintun.dll"];

    /// <summary>
    /// The version of the package whose files <see cref="FileHashes"/> knows; a new version of the component needs
    /// new hashes.
    /// </summary>
    public const string HASHED_VERSION = "3.1.0";

    /// <summary>
    /// The SHA-256 of the files of the official x64 package of <see cref="HASHED_VERSION"/>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> _x64Hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [EXECUTABLE] = "ba446f6e1a4093e43a65d6ff45f4b8c7b6485dc419327eedaa1a218549740e3a",
        ["awg.exe"] = "272badace73caeb26dc42656f318b3eb7f10028c2f76faad1f52d6fe1e0ced12",
        ["wintun.dll"] = "e5da8447dc2c320edc0fc52fa01885c103de8c118481f683643cacc3220dafce",
    };

    /// <summary>
    /// The SHA-256 of the files of the official ARM64 package of <see cref="HASHED_VERSION"/>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> _arm64Hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [EXECUTABLE] = "25fa81103152699bf416f394059b40d447001c3d0dea5ef889a9426f315e4c0a",
        ["awg.exe"] = "3106950ce9f0b93eb4c3f7975cbf44af41623ffeed630f19909730cd16d36e43",
        ["wintun.dll"] = "f7ba89005544be9d85231a9e0d5f23b2d15b3311667e2dad0debd344918a3f80",
    };

    /// <summary>
    /// Returns the SHA-256 of every file of the package of a processor: what the files in the data folder must be
    /// before a program with administrator rights starts them.
    /// </summary>
    /// <param name="runtimeIdentifier">The runtime identifier (<c>win-x64</c>, <c>win-arm64</c>).</param>
    /// <returns>The hashes by file name, or <see langword="null"/> when no package of that processor is known.</returns>
    public static IReadOnlyDictionary<string, string>? FileHashes(string runtimeIdentifier)
        => runtimeIdentifier switch
        {
            "win-x64" => _x64Hashes,
            "win-arm64" => _arm64Hashes,
            _ => null,
        };

    /// <summary>
    /// Returns the path of <see cref="EXECUTABLE"/> inside an installed component, whether it lies in the component
    /// directory itself or in the <see cref="DIRECTORY_NAME"/> folder of it.
    /// </summary>
    /// <param name="componentDirectory">The directory of the installed component.</param>
    /// <returns>The path, or <see langword="null"/> when the program is not there.</returns>
    public static string? FindExecutable(string? componentDirectory)
    {
        if (string.IsNullOrEmpty(componentDirectory))
        {
            return null;
        }

        var nested = Path.Combine(componentDirectory, DIRECTORY_NAME, EXECUTABLE);
        if (File.Exists(nested))
        {
            return nested;
        }

        var direct = Path.Combine(componentDirectory, EXECUTABLE);
        return File.Exists(direct) ? direct : null;
    }

    /// <summary>
    /// Checks whether a directory holds every file of the package.
    /// </summary>
    /// <param name="directory">The directory with the files.</param>
    /// <returns><see langword="true"/> when nothing is missing.</returns>
    public static bool IsComplete(string directory)
        => !string.IsNullOrEmpty(directory) && Files.All(name => File.Exists(Path.Combine(directory, name)));
}
