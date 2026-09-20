namespace Golether.UI.Services;

/// <summary>
/// Looks for a newer version of Golether and downloads it.
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// Gets or sets the address of the update manifest; an empty address switches the check off.
    /// </summary>
    string ManifestUrl { get; set; }

    /// <summary>
    /// Gets the version of this application.
    /// </summary>
    Version CurrentVersion { get; }

    /// <summary>
    /// Asks the source whether a newer version exists.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The newer release, or <see langword="null"/>.</returns>
    Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Downloads a release into the folder and checks its SHA-256.
    /// </summary>
    /// <param name="update">The release.</param>
    /// <param name="folder">The folder to download into.</param>
    /// <param name="progress">Receives the progress, 0–1.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The path of the downloaded file.</returns>
    /// <exception cref="InvalidDataException">The file does not match the expected hash.</exception>
    Task<string> DownloadAsync(UpdateInfo update, string folder, IProgress<double>? progress, CancellationToken cancellationToken);
}
