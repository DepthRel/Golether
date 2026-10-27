using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Golether.Core.Configuration;
using Golether.Localization;
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
    /// The pictures a folder shows, offered by the file picker next to the videos.
    /// </summary>
    private static readonly string[] _picturePatterns =
        ["*.jpg", "*.jpeg", "*.jfif", "*.png", "*.webp", "*.gif", "*.bmp", "*.heic", "*.heif", "*.hif", "*.avif", "*.tif", "*.tiff"];

    /// <summary>
    /// Initializes a new instance of the <see cref="AvaloniaDialogService"/> class.
    /// </summary>
    /// <param name="owner">The owner window.</param>
    public AvaloniaDialogService(Window owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <summary>
    /// Gets the window a new dialog should belong to: the active one (so a dialog opened from the settings window
    /// belongs to it, not to the disabled main window), or the main window when none is active.
    /// </summary>
    private Window ActiveOwner
        => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.FirstOrDefault(w => w.IsActive) ?? _owner;

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
    public async Task ShowErrorAsync(string title, string message)
    {
        var dialog = MessageDialog.ForError(title, message);
        await dialog.ShowDialog(ActiveOwner);

        if (dialog.ReportRequested)
        {
            await ReportProblemAsync(message.Length > 0 ? title + "\n" + message : title);
        }
    }

    /// <inheritdoc />
    public async Task ReportProblemAsync(string? details)
    {
        if (await ActiveOwner.Launcher.LaunchUriAsync(ProblemReport.CreateMailto(details)))
        {
            return;
        }

        // No mail program took the letter: the address is offered to write from anywhere else.
        var title = Texts.Get("Problem.NoMail.Title");
        var text = Texts.Format("Problem.NoMail.Text", ProjectSite.FEEDBACK_EMAIL);
        if (await ConfirmAsync(title, text, Texts.Get("Problem.NoMail.Copy")))
        {
            await CopyTextAsync(ProjectSite.FEEDBACK_EMAIL);
        }
    }

    /// <inheritdoc />
    public Task ShowAboutAsync(AboutViewModel viewModel)
        => new AboutDialog { DataContext = viewModel }.ShowDialog(ActiveOwner);

    /// <inheritdoc />
    public Task ShowMessageAsync(string title, string message)
        => new MessageDialog(title, message).ShowDialog(ActiveOwner);

    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(string title, string message, string acceptText)
        => await new MessageDialog(title, message, acceptText).ShowDialog<bool?>(ActiveOwner) == true;

    /// <inheritdoc />
    public async Task<bool> OpenLinkAsync(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
            await _owner.Launcher.LaunchUriAsync(uri);

    /// <inheritdoc />
    public void BringToFront()
    {
        if (_owner.WindowState == WindowState.Minimized)
        {
            _owner.WindowState = WindowState.Normal;
        }

        _owner.Activate();
    }

    /// <inheritdoc />
    public async Task<string?> PickMediaFileAsync()
    {
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Texts.Get("Dialog.PickVideo.Title"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Texts.Get("Dialog.PickVideo.Filter"))
                {
                    Patterns = ["*.mkv", "*.mp4", "*.m4v", "*.avi", "*.mov", "*.webm", "*.ts", "*.m2ts", "*.wmv", "*.flv", .. _picturePatterns],
                },
                new FilePickerFileType(Texts.Get("Dialog.PickVideo.Pictures")) { Patterns = _picturePatterns },
                FilePickerFileTypes.All,
            ],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <inheritdoc />
    public Task<GalleryChoice?> ChooseGalleryAsync(FolderBrowserViewModel viewModel)
        => new FolderBrowserDialog { DataContext = viewModel }.ShowDialog<GalleryChoice?>(ActiveOwner);

    /// <inheritdoc />
    public async Task<string?> PickSubtitleFileAsync()
    {
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Texts.Get("Dialog.PickSubtitles.Title"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Texts.Get("Dialog.PickSubtitles.Filter")) { Patterns = ["*.srt", "*.ass", "*.ssa", "*.vtt", "*.sub", "*.sup", "*.idx"] },
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
            Title = Texts.Get("Dialog.PickAudio.Title"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Texts.Get("Dialog.PickAudio.Filter")) { Patterns = ["*.mka", "*.mp3", "*.aac", "*.ac3", "*.dts", "*.flac", "*.opus", "*.wav", "*.m4a"] },
                FilePickerFileTypes.All,
            ],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <inheritdoc />
    public async Task<string?> PickImageFileAsync()
    {
        var files = await ActiveOwner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Texts.Get("Dialog.PickImage.Title"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Texts.Get("Dialog.PickImage.Filter")) { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp", "*.gif"] },
                FilePickerFileTypes.All,
            ],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <inheritdoc />
    public Task<AvatarSelection?> ShowAvatarAsync(AvatarDialogViewModel viewModel)
        => new AvatarDialog { DataContext = viewModel }.ShowDialog<AvatarSelection?>(ActiveOwner);

    /// <inheritdoc />
    public Task ShowSettingsAsync(SettingsViewModel viewModel)
        => new SettingsDialog { DataContext = viewModel }.ShowDialog(_owner);

    /// <inheritdoc />
    public async Task<string?> PickConfigSavePathAsync(string suggestedName)
    {
        var file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Texts.Get("Dialog.SaveConfig.Title"),
            SuggestedFileName = suggestedName,
            DefaultExtension = "conf",
            FileTypeChoices = [new FilePickerFileType(Texts.Get("Dialog.SaveConfig.Filter")) { Patterns = ["*.conf"] }],
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
