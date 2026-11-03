using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Client.ViewModels;
using Golether.Localization;
using Golether.Media.Streaming.Gallery;
using Golether.Mobile.Services;

namespace Golether.Mobile.ViewModels;

/// <summary>
/// What the host chose to show: a film, or a folder of pictures and videos. The files are chosen in the pickers of the
/// system and read through it.
/// </summary>
public sealed partial class MediaChoiceViewModel : ObservableObject
{
    /// <summary>
    /// The types of files the picker of a film offers.
    /// </summary>
    private static readonly string[] _videoTypes = ["video/*"];

    /// <summary>
    /// The system.
    /// </summary>
    private readonly IMobilePlatform _platform;

    /// <summary>
    /// Gives the file picker of the system.
    /// </summary>
    private readonly Func<IStorageProvider?> _storage;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaChoiceViewModel"/> class.
    /// </summary>
    /// <param name="platform">The system.</param>
    /// <param name="storage">Gives the file picker of the system.</param>
    public MediaChoiceViewModel(IMobilePlatform platform, Func<IStorageProvider?> storage)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    /// <summary>
    /// Gets or sets the path of the chosen film, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChoice), nameof(IsFilm))]
    public partial string? FilmPath { get; set; }

    /// <summary>
    /// Gets or sets the chosen folder, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChoice), nameof(IsFolder))]
    public partial GalleryFolder? Folder { get; set; }

    /// <summary>
    /// Gets or sets the name of what was chosen.
    /// </summary>
    [ObservableProperty]
    public partial string ChosenName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets what a chosen folder shows.
    /// </summary>
    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets what of a chosen folder is not shown, empty when everything is.
    /// </summary>
    [ObservableProperty]
    public partial string LeftOut { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the place of shooting, the camera and the comments are left out of the
    /// pictures. A photo of a phone almost always carries the place.
    /// </summary>
    [ObservableProperty]
    public partial bool StripMetadata { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a chosen folder is being read.
    /// </summary>
    [ObservableProperty]
    public partial bool IsReading { get; set; }

    /// <summary>
    /// Gets or sets why the last choice could not be taken, empty when it could.
    /// </summary>
    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether something to show was chosen.
    /// </summary>
    public bool HasChoice => FilmPath is not null || Folder is not null;

    /// <summary>
    /// Gets a value indicating whether a film was chosen.
    /// </summary>
    public bool IsFilm => FilmPath is not null;

    /// <summary>
    /// Gets a value indicating whether a folder was chosen.
    /// </summary>
    public bool IsFolder => Folder is not null;

    /// <summary>
    /// Forgets the choice, for the next one.
    /// </summary>
    public void Clear()
    {
        FilmPath = null;
        Folder = null;
        ChosenName = string.Empty;
        Summary = string.Empty;
        LeftOut = string.Empty;
        Error = string.Empty;
        StripMetadata = true;
    }

    /// <summary>
    /// Lets the user choose a film.
    /// </summary>
    /// <returns>A task that completes when the film is chosen or the choice was given up.</returns>
    [RelayCommand]
    private async Task PickFilmAsync()
    {
        if (_storage() is not { } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Texts.Get("Main.PickFile"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(Texts.Get("Main.PickFile")) { MimeTypes = _videoTypes }],
        });
        if (files.Count == 0)
        {
            return;
        }

        using var file = files[0];
        try
        {
            var path = await Task.Run(() => _platform.OpenDocument(file.Path));

            Clear();
            FilmPath = path;
            ChosenName = Path.GetFileName(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Error = ex.Message;
        }
    }

    /// <summary>
    /// Lets the user choose a folder and counts what it shows.
    /// </summary>
    /// <returns>A task that completes when the folder is counted or the choice was given up.</returns>
    [RelayCommand]
    private async Task PickFolderAsync()
    {
        if (_storage() is not { } storage)
        {
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Texts.Get("Gallery.PickFolder"),
            AllowMultiple = false,
        });
        if (folders.Count == 0)
        {
            return;
        }

        using var chosen = folders[0];
        Error = string.Empty;
        IsReading = true;
        try
        {
            // The pictures this device cannot turn into JPEG (HEIC, AVIF, TIFF) are left out and counted.
            var folder = await Task.Run(() => GalleryFolder.Describe(_platform.OpenFolder(chosen.Path), canDecode: false));
            if (folder.Files.Count == 0)
            {
                Error = Texts.Get("Gallery.Browser.Nothing");
                return;
            }

            Clear();
            Folder = folder;
            ChosenName = folder.Title;
            Summary = DisplayFormat.GallerySummary(folder);
            LeftOut = DisplayFormat.GalleryLeftOut(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Error = Texts.Format("Gallery.Browser.CannotOpen", ex.Message);
        }
        finally
        {
            IsReading = false;
        }
    }
}
