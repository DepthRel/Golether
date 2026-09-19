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
    public const string DirectoryName = "amneziawg";

    /// <summary>
    /// The program that raises and drops tunnels.
    /// </summary>
    public const string Executable = "amneziawg.exe";

    /// <summary>
    /// The files taken out of the package: the tunnel program, the command-line tool and the virtual adapter driver.
    /// </summary>
    public static IReadOnlyList<string> Files { get; } = [Executable, "awg.exe", "wintun.dll"];

    /// <summary>
    /// Returns the path of <see cref="Executable"/> inside an installed component, whether it lies in the component
    /// directory itself or in the <see cref="DirectoryName"/> folder of it.
    /// </summary>
    /// <param name="componentDirectory">The directory of the installed component.</param>
    /// <returns>The path, or <see langword="null"/> when the program is not there.</returns>
    public static string? FindExecutable(string? componentDirectory)
    {
        if (string.IsNullOrEmpty(componentDirectory))
        {
            return null;
        }

        var nested = Path.Combine(componentDirectory, DirectoryName, Executable);
        if (File.Exists(nested))
        {
            return nested;
        }

        var direct = Path.Combine(componentDirectory, Executable);
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
