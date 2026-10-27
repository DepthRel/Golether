using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Core.Enums;
using Golether.Core.Media;

namespace Golether.UI.ViewModels;

/// <summary>
/// An item of a shown folder in the strip and in the grid: its name, its small picture and who is looking at it.
/// </summary>
public sealed partial class GalleryItemViewModel : ObservableObject
{
    /// <summary>
    /// Asks for the small picture; called the first time a view shows the item.
    /// </summary>
    private readonly Action<GalleryItemViewModel> _requestThumbnail;

    /// <summary>
    /// Whether the small picture was asked for already.
    /// </summary>
    private bool _requested;

    /// <summary>
    /// The small picture, or <see langword="null"/> while it has not arrived.
    /// </summary>
    private Bitmap? _thumbnail;

    /// <summary>
    /// Initializes a new instance of the <see cref="GalleryItemViewModel"/> class.
    /// </summary>
    /// <param name="index">The index of the item in the showing.</param>
    /// <param name="requestThumbnail">Asks for the small picture.</param>
    /// <param name="showToAll">Puts the item on everybody's screen.</param>
    /// <param name="openAlone">Opens the picture on this screen only.</param>
    public GalleryItemViewModel(int index, Action<GalleryItemViewModel> requestThumbnail, Func<GalleryItemViewModel, Task> showToAll, Action<GalleryItemViewModel> openAlone)
    {
        Index = index;
        _requestThumbnail = requestThumbnail;
        ShowToAllCommand = new AsyncRelayCommand(() => showToAll(this));
        OpenAloneCommand = new RelayCommand(() => openAlone(this), () => !IsVideo);
    }

    /// <summary>
    /// Gets the command that puts the item on everybody's screen.
    /// </summary>
    public IAsyncRelayCommand ShowToAllCommand { get; }

    /// <summary>
    /// Gets the command that opens the picture on this screen only; a video is watched together or not at all.
    /// </summary>
    public IRelayCommand OpenAloneCommand { get; }

    /// <summary>
    /// Gets the index of the item in the showing.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Gets the number shown on the item, counted from one.
    /// </summary>
    public string Number => (Index + 1).ToString(CultureInfo.CurrentCulture);

    /// <summary>
    /// Gets the file name, or an empty text while it has not arrived.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullName))]
    public partial string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the nested folder of the item, relative to the shown folder; empty for the shown folder itself.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullName))]
    public partial string Folder { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the name with the nested folder in front: "Day 2 / IMG_0031.jpg".
    /// </summary>
    public string FullName => Folder.Length == 0 ? Name : FullFolder(Folder) + " / " + Name;

    /// <summary>
    /// Gets the nested folder this item opens in the strip, or an empty text when it continues the folder of the item
    /// before it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartsFolder))]
    public partial string FolderLabel { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether this item is the first of a nested folder.
    /// </summary>
    public bool StartsFolder => FolderLabel.Length > 0;

    /// <summary>
    /// Gets a value indicating whether the item is a video, which becomes the film when it is put on the screen.
    /// </summary>
    [ObservableProperty]
    public partial bool IsVideo { get; private set; }

    /// <summary>
    /// Gets the small picture. The first time a view asks for it, it is requested: a strip of two thousand items asks
    /// only for those scrolled into view.
    /// </summary>
    public Bitmap? Thumbnail
    {
        get
        {
            if (!_requested)
            {
                _requested = true;
                _requestThumbnail(this);
            }

            return _thumbnail;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the item is on everybody's screen.
    /// </summary>
    [ObservableProperty]
    public partial bool IsStage { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the item is the one on this screen.
    /// </summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; }

    /// <summary>
    /// Gets the participants looking at this item.
    /// </summary>
    public ObservableCollection<ParticipantItemViewModel> Viewers { get; } = [];

    /// <summary>
    /// Takes in what the host said about the item.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="previousFolder">The nested folder of the item before it, or <see langword="null"/> for the first.</param>
    public void SetInfo(GalleryItem item, string? previousFolder)
    {
        ArgumentNullException.ThrowIfNull(item);
        Name = item.FileName;
        Folder = item.Folder;
        IsVideo = item.Kind == GalleryItemKind.Video;
        FolderLabel = item.Folder.Length > 0 && !string.Equals(item.Folder, previousFolder, StringComparison.Ordinal) ? FullFolder(item.Folder) : string.Empty;
        OpenAloneCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Puts the small picture in place once it arrived.
    /// </summary>
    /// <param name="thumbnail">The small picture.</param>
    public void SetThumbnail(Bitmap? thumbnail)
    {
        _thumbnail = thumbnail;
        OnPropertyChanged(nameof(Thumbnail));
    }

    /// <summary>
    /// Writes a nested folder the way a person reads it.
    /// </summary>
    /// <param name="folder">The folder with its separators.</param>
    /// <returns>The folder with spaced separators.</returns>
    private static string FullFolder(string folder) => folder.Replace(GalleryItem.FOLDER_SEPARATOR.ToString(), " / ", StringComparison.Ordinal);
}
