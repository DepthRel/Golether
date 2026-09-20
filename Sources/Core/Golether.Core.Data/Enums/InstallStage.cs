namespace Golether.Core.Data.Enums;

/// <summary>
/// A step of an installation.
/// </summary>
public enum InstallStage
{
    /// <summary>
    /// Downloading the package.
    /// </summary>
    Downloading = 0,

    /// <summary>
    /// Checking the checksum.
    /// </summary>
    Verifying = 1,

    /// <summary>
    /// Unpacking or running the installer.
    /// </summary>
    Unpacking = 2,

    /// <summary>
    /// Keeping only the needed files.
    /// </summary>
    Finishing = 3,

    /// <summary>
    /// Done.
    /// </summary>
    Completed = 4,
}
