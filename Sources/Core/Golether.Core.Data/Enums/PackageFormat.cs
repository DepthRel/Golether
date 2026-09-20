namespace Golether.Core.Data.Enums;

/// <summary>
/// The format of a downloadable package.
/// </summary>
public enum PackageFormat
{
    /// <summary>
    /// A 7-Zip archive (libmpv builds for Windows), extracted with the tar tool of Windows.
    /// </summary>
    SevenZip = 0,

    /// <summary>
    /// An Inno Setup installer (GStreamer for Windows), run silently for the current user and reduced to the runtime.
    /// </summary>
    InnoSetup = 1,

    /// <summary>
    /// A Windows Installer package (AmneziaWG for Windows). Only its files are taken out, with
    /// <c>msiexec /a</c>: nothing is installed into the system and no administrator rights are needed.
    /// </summary>
    WindowsInstaller = 2,
}
