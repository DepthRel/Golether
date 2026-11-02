namespace Golether.Client.Services;

/// <summary>
/// The dialogs every client offers, whatever its windows look like: errors and the pickers of track files.
/// </summary>
public interface ICommonDialogs
{
    /// <summary>
    /// Shows an error; the user can report it to the project from the same window.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="message">The message.</param>
    /// <returns>A task that completes when the dialog is closed.</returns>
    Task ShowErrorAsync(string title, string message);

    /// <summary>
    /// Asks for a subtitle file.
    /// </summary>
    /// <returns>The path, or <see langword="null"/> when cancelled.</returns>
    Task<string?> PickSubtitleFileAsync();

    /// <summary>
    /// Asks for a sound file (an external dubbing).
    /// </summary>
    /// <returns>The path, or <see langword="null"/> when cancelled.</returns>
    Task<string?> PickAudioFileAsync();
}
