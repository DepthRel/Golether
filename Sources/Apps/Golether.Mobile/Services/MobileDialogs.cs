using Avalonia.Platform.Storage;
using Golether.Client.Services;
using Golether.Localization;

namespace Golether.Mobile.Services;

/// <summary>
/// The common dialogs on a phone: an error goes to the line on the screen, a file comes from the picker of the system.
/// </summary>
public sealed class MobileDialogs : ICommonDialogs
{
    /// <summary>
    /// The largest subtitle file: even a long film with heavy styling stays far below it.
    /// </summary>
    private const long MAX_SUBTITLE_SIZE = 64L * 1024 * 1024;

    /// <summary>
    /// The largest sound file: a lossless track of a long film fits, and the copy still has to fit the phone.
    /// </summary>
    private const long MAX_AUDIO_SIZE = 4L * 1024 * 1024 * 1024;

    /// <summary>
    /// Gives the file picker of the system.
    /// </summary>
    private readonly Func<IStorageProvider?> _storage;

    /// <summary>
    /// Keeps the chosen files.
    /// </summary>
    private readonly ImportedTrackFiles _files;

    /// <summary>
    /// Shows a text to the user.
    /// </summary>
    private readonly Action<string> _showError;

    /// <summary>
    /// Initializes a new instance of the <see cref="MobileDialogs"/> class.
    /// </summary>
    /// <param name="storage">Gives the file picker of the system.</param>
    /// <param name="files">Keeps the chosen files.</param>
    /// <param name="showError">Shows a text to the user.</param>
    public MobileDialogs(Func<IStorageProvider?> storage, ImportedTrackFiles files, Action<string> showError)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _showError = showError ?? throw new ArgumentNullException(nameof(showError));
    }

    /// <inheritdoc />
    public Task ShowErrorAsync(string title, string message)
    {
        _showError($"{title}: {message}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> PickSubtitleFileAsync() => PickAsync("Dialog.PickSubtitles.Title", "Tracks.Error.SubtitlesTitle", MAX_SUBTITLE_SIZE);

    /// <inheritdoc />
    public Task<string?> PickAudioFileAsync() => PickAsync("Dialog.PickAudio.Title", "Tracks.Error.AudioTitle", MAX_AUDIO_SIZE);

    /// <summary>
    /// Lets the user choose a file and copies it for the player. The system picker knows types of files, not extensions,
    /// and subtitles have no type of their own, so it shows everything.
    /// </summary>
    /// <param name="titleKey">The title of the picker.</param>
    /// <param name="failureKey">The text of a failure.</param>
    /// <param name="maxSize">The largest allowed size in bytes.</param>
    /// <returns>The path, or <see langword="null"/> when there is no file.</returns>
    private async Task<string?> PickAsync(string titleKey, string failureKey, long maxSize)
    {
        if (_storage() is not { } storage)
        {
            return null;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Texts.Get(titleKey), AllowMultiple = false });
        if (files.Count == 0)
        {
            return null;
        }

        using var file = files[0];
        try
        {
            await using var source = await file.OpenReadAsync();
            return await _files.ImportAsync(source, file.Name, maxSize, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await ShowErrorAsync(Texts.Get(failureKey), ex.Message);
            return null;
        }
    }
}
