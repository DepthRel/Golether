using System.Runtime.InteropServices;

namespace Golether.UI.Services;

/// <summary>
/// What the application tells about itself: its version and the system it runs on.
/// </summary>
public static class AppInfo
{
    /// <summary>
    /// Gets the version of the application, as three numbers.
    /// </summary>
    public static string Version { get; } =
        (typeof(AppInfo).Assembly.GetName().Version ?? new Version(0, 0, 0)).ToString(3);

    /// <summary>
    /// Gets the operating system and the processor architecture, for a problem report.
    /// </summary>
    public static string System { get; } = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
}
