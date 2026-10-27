using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Golether.UI.Enums;

namespace Golether.UI.ViewModels.Dialogs;

/// <summary>
/// An entry of the folder browser: a nested folder, a picture or a video.
/// </summary>
public sealed class FolderEntryViewModel : ObservableObject
{
    /// <summary>
    /// Asks for the small picture; called the first time the browser shows the entry, or <see langword="null"/> when
    /// it has none.
    /// </summary>
    private readonly Action<FolderEntryViewModel>? _requestThumbnail;

    /// <summary>
    /// Whether the small picture was asked for already.
    /// </summary>
    private bool _requested;

    /// <summary>
    /// The small picture, or <see langword="null"/> while it has not been made.
    /// </summary>
    private Bitmap? _thumbnail;

    /// <summary>
    /// Initializes a new instance of the <see cref="FolderEntryViewModel"/> class.
    /// </summary>
    /// <param name="name">The name shown.</param>
    /// <param name="path">The full path.</param>
    /// <param name="kind">What the entry is.</param>
    /// <param name="requestThumbnail">Asks for the small picture, or <see langword="null"/> when it has none.</param>
    public FolderEntryViewModel(string name, string path, FolderEntryKind kind, Action<FolderEntryViewModel>? requestThumbnail)
    {
        Name = name;
        Path = path;
        Kind = kind;
        _requestThumbnail = requestThumbnail;
    }

    /// <summary>
    /// Gets the name shown.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the full path.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets what the entry is.
    /// </summary>
    public FolderEntryKind Kind { get; }

    /// <summary>
    /// Gets a value indicating whether the entry is a nested folder.
    /// </summary>
    public bool IsFolder => Kind == FolderEntryKind.Folder;

    /// <summary>
    /// Gets a value indicating whether the entry is a video.
    /// </summary>
    public bool IsVideo => Kind == FolderEntryKind.Video;

    /// <summary>
    /// Gets a value indicating whether the entry is a picture without a small picture (yet): its sign is shown.
    /// </summary>
    public bool ShowsPictureSign => Kind == FolderEntryKind.Picture && _thumbnail is null;

    /// <summary>
    /// Gets the small picture of a picture. The first time the browser asks for it, it is made: a folder of thousands
    /// of photographs decodes only those scrolled into view.
    /// </summary>
    public Bitmap? Thumbnail
    {
        get
        {
            if (!_requested && _requestThumbnail is not null)
            {
                _requested = true;
                _requestThumbnail(this);
            }

            return _thumbnail;
        }
    }

    /// <summary>
    /// Puts the small picture in place once it was made.
    /// </summary>
    /// <param name="thumbnail">The small picture.</param>
    public void SetThumbnail(Bitmap? thumbnail)
    {
        _thumbnail = thumbnail;
        OnPropertyChanged(nameof(Thumbnail));
        OnPropertyChanged(nameof(ShowsPictureSign));
    }
}
