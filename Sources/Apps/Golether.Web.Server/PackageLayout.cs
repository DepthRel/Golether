namespace Golether.Web.Server;

/// <summary>
/// The folders of a packaged server: the launcher in the root, the program and its libraries in <c>bin</c>, the
/// settings in <c>config</c>, the documentation in <c>docs</c>. A build started from the sources keeps everything in one
/// folder instead.
/// </summary>
internal static class PackageLayout
{
    /// <summary>
    /// The folder of the program and its libraries in a package.
    /// </summary>
    public const string ProgramFolder = "bin";

    /// <summary>
    /// The folder of the settings in a package.
    /// </summary>
    public const string ConfigFolder = "config";

    /// <summary>
    /// Finds the folder with <c>appsettings.json</c>.
    /// </summary>
    /// <param name="programDirectory">The folder of the program (<see cref="AppContext.BaseDirectory"/>).</param>
    /// <returns>
    /// <c>config</c> next to the program folder when the program runs from the <c>bin</c> folder of a package that has
    /// one, otherwise the program folder itself.
    /// </returns>
    public static string FindConfigDirectory(string programDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programDirectory);
        var program = Path.GetFullPath(programDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(Path.GetFileName(program), ProgramFolder, StringComparison.OrdinalIgnoreCase)
            && Path.GetDirectoryName(program) is { } root
            && Directory.Exists(Path.Combine(root, ConfigFolder)))
        {
            return Path.Combine(root, ConfigFolder);
        }

        return program;
    }
}
