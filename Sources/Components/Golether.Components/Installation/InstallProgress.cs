namespace Golether.Components.Installation;

/// <summary>
/// Progress of an installation.
/// </summary>
/// <param name="Stage">The current step.</param>
/// <param name="Bytes">The bytes downloaded so far.</param>
/// <param name="TotalBytes">The download size.</param>
public readonly record struct InstallProgress(InstallStage Stage, long Bytes, long TotalBytes)
{
    /// <summary>
    /// Gets the overall fraction, 0–1 (the download is weighted as 80 %).
    /// </summary>
    public double Fraction => Stage switch
    {
        InstallStage.Downloading => TotalBytes > 0 ? 0.8 * Bytes / TotalBytes : 0,
        InstallStage.Verifying => 0.82,
        InstallStage.Unpacking => 0.85,
        InstallStage.Finishing => 0.95,
        _ => 1,
    };
}
