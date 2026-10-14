using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Golether.Localization;

namespace Golether.Web.Server.UI.Services;

/// <summary>
/// The questions the monitor asks the user.
/// </summary>
public interface IDialogs
{
    /// <summary>
    /// Asks where to save a file.
    /// </summary>
    /// <param name="suggestedName">The name offered.</param>
    /// <returns>The chosen file, or <see langword="null"/> when the user cancelled.</returns>
    Task<Stream?> SaveFileAsync(string suggestedName);

    /// <summary>
    /// Asks the user to confirm an action that is hard to undo.
    /// </summary>
    /// <param name="title">The question.</param>
    /// <param name="message">What the action does.</param>
    /// <param name="acceptText">The caption of the button that goes ahead.</param>
    /// <returns><see langword="true"/> when the user went ahead.</returns>
    Task<bool> ConfirmAsync(string title, string message, string acceptText);
}

/// <summary>
/// The questions of the monitor as windows of the system.
/// </summary>
/// <param name="owner">The main window.</param>
public sealed class AvaloniaDialogs(Window owner) : IDialogs
{
    /// <inheritdoc />
    public async Task<Stream?> SaveFileAsync(string suggestedName)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Texts.Get("Monitor.Export.Title"),
            SuggestedFileName = suggestedName,
            DefaultExtension = "log",
            FileTypeChoices = [new FilePickerFileType(Texts.Get("Monitor.Export.FileType")) { Patterns = ["*.log", "*.txt"] }],
        });
        return file is null ? null : await file.OpenWriteAsync();
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(string title, string message, string acceptText)
        => await new Views.ConfirmDialog(title, message, acceptText).ShowDialog<bool?>(owner) == true;
}
