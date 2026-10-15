using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Golether.Localization;

namespace Golether.Web.Server.UI.Services;

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
