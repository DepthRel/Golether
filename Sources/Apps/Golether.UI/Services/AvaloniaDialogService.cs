using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Golether.Security.Admission;
using Golether.UI.ViewModels.Dialogs;
using Golether.UI.Views.Dialogs;

namespace Golether.UI.Services;

/// <summary>
/// <see cref="IDialogService"/> with Avalonia windows owned by the main window.
/// </summary>
public sealed class AvaloniaDialogService : IDialogService
{
    /// <summary>
    /// The owner window.
    /// </summary>
    private readonly Window _owner;

    /// <summary>
    /// Initializes a new instance of the <see cref="AvaloniaDialogService"/> class.
    /// </summary>
    /// <param name="owner">The owner window.</param>
    public AvaloniaDialogService(Window owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmAdmissionAsync(AdmissionRequest request, CancellationToken cancellationToken)
    {
        var dialog = new AdmissionDialog { DataContext = new AdmissionDialogViewModel(request) };
        await using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
        _owner.Activate();
        var result = await dialog.ShowDialog<bool?>(_owner);
        cancellationToken.ThrowIfCancellationRequested();
        return result == true;
    }

    /// <inheritdoc />
    public Task ShowInviteAsync(InviteDialogViewModel viewModel)
        => new InviteDialog { DataContext = viewModel }.ShowDialog(_owner);

    /// <inheritdoc />
    public Task ShowTunnelsAsync(TunnelDialogViewModel viewModel)
        => new TunnelDialog { DataContext = viewModel }.ShowDialog(_owner);

    /// <inheritdoc />
    public Task ShowErrorAsync(string title, string message)
        => new MessageDialog(title, message).ShowDialog(_owner);

    /// <inheritdoc />
    public Task ShowMessageAsync(string title, string message)
        => new MessageDialog(title, message).ShowDialog(_owner);

    /// <inheritdoc />
    public async Task<string?> PickMediaFileAsync()
    {
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите видео",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Видео") { Patterns = ["*.mkv", "*.mp4", "*.m4v", "*.avi", "*.mov", "*.webm", "*.ts", "*.m2ts", "*.wmv", "*.flv"] },
                FilePickerFileTypes.All,
            ],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <inheritdoc />
    public async Task<string?> PickSubtitleFileAsync()
    {
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите субтитры",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Субтитры") { Patterns = ["*.srt", "*.ass", "*.ssa", "*.vtt", "*.sub", "*.sup", "*.idx"] },
                FilePickerFileTypes.All,
            ],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <inheritdoc />
    public async Task<string?> PickAudioFileAsync()
    {
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите звуковую дорожку",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Звук") { Patterns = ["*.mka", "*.mp3", "*.aac", "*.ac3", "*.dts", "*.flac", "*.opus", "*.wav", "*.m4a"] },
                FilePickerFileTypes.All,
            ],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <inheritdoc />
    public async Task<string?> PickConfigSavePathAsync(string suggestedName)
    {
        var file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Сохранить конфигурацию AmneziaWG",
            SuggestedFileName = suggestedName,
            DefaultExtension = "conf",
            FileTypeChoices = [new FilePickerFileType("Конфигурация AmneziaWG") { Patterns = ["*.conf"] }],
        });
        return file?.TryGetLocalPath();
    }

    /// <inheritdoc />
    public async Task CopyTextAsync(string text)
    {
        if (_owner.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    /// <inheritdoc />
    public async Task<string?> PasteTextAsync()
        => _owner.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
}
