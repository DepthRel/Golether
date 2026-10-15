namespace Golether.Web.Server.UI.Services;

/// <summary>
/// Where the server the monitor belongs to keeps its settings.
/// </summary>
/// <param name="SettingsFile">The full path of <c>config/appsettings.json</c>.</param>
public sealed record ServerPaths(string SettingsFile)
{
    /// <summary>
    /// The settings file inside the folder of the server.
    /// </summary>
    private static readonly string _relative = Path.Combine("config", "appsettings.json");

    /// <summary>
    /// Finds the settings of the server. The monitor lies in <c>monitor/</c> of the server folder (in every package
    /// and in every installation), so the settings are one level up. Otherwise the usual installation folder of the
    /// system is taken.
    /// </summary>
    /// <param name="baseDirectory">The folder of the monitor.</param>
    /// <param name="exists">Tells whether a file exists; replaced in tests.</param>
    /// <returns>The paths.</returns>
    public static ServerPaths Find(string baseDirectory, Func<string, bool>? exists = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseDirectory);
        exists ??= File.Exists;
        for (var folder = new DirectoryInfo(baseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, _relative);
            if (exists(candidate))
            {
                return new ServerPaths(candidate);
            }
        }

        var installed = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Golether Server")
            : OperatingSystem.IsMacOS() ? "/usr/local/golether-server" : "/opt/golether-server";
        return new ServerPaths(Path.Combine(installed, _relative));
    }
}
