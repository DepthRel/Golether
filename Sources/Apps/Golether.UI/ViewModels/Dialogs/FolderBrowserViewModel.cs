using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Core.Media;
using Golether.Localization;
using Golether.Media.Streaming.Gallery;
using Golether.UI.Enums;
using Golether.UI.Services;

namespace Golether.UI.ViewModels.Dialogs;

/// <summary>
/// Chooses the folder the host shows. Unlike the folder picker of the system, it shows what lies in a folder (its
/// nested folders, its pictures with their small pictures and its videos) and counts what would be shown, with the
/// nested folders or without them, before anything is read or sent.
/// </summary>
public sealed partial class FolderBrowserViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// The most entries listed in one folder; the count of what is shown is not limited by it.
    /// </summary>
    private const int MAX_LISTED = 5000;

    /// <summary>
    /// The longest side a small picture of the browser is decoded at.
    /// </summary>
    private const int THUMBNAIL_SIDE = 160;

    /// <summary>
    /// The most small pictures decoded at once.
    /// </summary>
    private const int THUMBNAIL_PARALLELISM = 3;

    /// <summary>
    /// Decodes the small pictures.
    /// </summary>
    private readonly IPictureDecoder _decoder;

    /// <summary>
    /// Whether HEIC, AVIF and TIFF pictures can be shown on this device.
    /// </summary>
    private readonly bool _canDecode;

    /// <summary>
    /// Limits the small pictures decoded at once.
    /// </summary>
    private readonly SemaphoreSlim _thumbnailSlots = new(THUMBNAIL_PARALLELISM);

    /// <summary>
    /// The folders left with a step to another folder, the latest on top: Back returns to them.
    /// </summary>
    private readonly Stack<string> _back = new();

    /// <summary>
    /// The folders left with Back, the latest on top: Forward returns to them.
    /// </summary>
    private readonly Stack<string> _forward = new();

    /// <summary>
    /// Cancelled when another folder is opened: the small pictures of this one are of no use any more.
    /// </summary>
    private CancellationTokenSource _listing = new();

    /// <summary>
    /// Cancelled when a new count starts.
    /// </summary>
    private CancellationTokenSource _counting = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="FolderBrowserViewModel"/> class.
    /// </summary>
    /// <param name="decoder">Decodes the small pictures.</param>
    /// <param name="canDecode">Whether HEIC, AVIF and TIFF pictures can be shown on this device.</param>
    /// <param name="start">The folder opened first, or <see langword="null"/> for the pictures of the user.</param>
    public FolderBrowserViewModel(IPictureDecoder decoder, bool canDecode, string? start)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _canDecode = canDecode;
        Places = BuildPlaces();
        var first = start is not null && Directory.Exists(start)
            ? start
            : Places.FirstOrDefault()?.Path ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Open(first);
    }

    /// <summary>
    /// Gets the folders of the user and the disks, opened with one click.
    /// </summary>
    public IReadOnlyList<FolderPlace> Places { get; }

    /// <summary>
    /// Gets the open folder.
    /// </summary>
    [ObservableProperty]
    public partial string CurrentPath { get; private set; } = string.Empty;

    /// <summary>
    /// Gets or sets the path in the address line; Enter opens it.
    /// </summary>
    [ObservableProperty]
    public partial string PathText { get; set; } = string.Empty;

    /// <summary>
    /// Gets the nested folders, pictures and videos of the open folder.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<FolderEntryViewModel> Entries { get; private set; } = [];

    /// <summary>
    /// Gets or sets the number of entries in a row; the view sets it from its width.
    /// </summary>
    [ObservableProperty]
    public partial int Columns { get; set; } = 5;

    /// <summary>
    /// Gets the rows of entries: only the rows in sight are made.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<IReadOnlyList<FolderEntryViewModel>> Rows { get; private set; } = [];

    /// <summary>
    /// Gets why the last folder could not be opened, or an empty text.
    /// </summary>
    [ObservableProperty]
    public partial string ErrorText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the nested folders are shown too.
    /// </summary>
    [ObservableProperty]
    public partial bool IncludeSubfolders { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the place, the camera and the comments are left out of the pictures.
    /// </summary>
    [ObservableProperty]
    public partial bool StripMetadata { get; set; } = true;

    /// <summary>
    /// Gets a value indicating whether what would be shown is being counted.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowCommand))]
    public partial bool IsCounting { get; private set; }

    /// <summary>
    /// Gets what would be shown, or <see langword="null"/> while it is counted.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowCommand))]
    public partial GalleryFolder? Folder { get; private set; }

    /// <summary>
    /// Gets how many pictures and videos would be shown.
    /// </summary>
    [ObservableProperty]
    public partial string SummaryText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets what of the folder would be left out, or an empty text.
    /// </summary>
    [ObservableProperty]
    public partial string NotesText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the choice once the host confirmed it, or <see langword="null"/>.
    /// </summary>
    public GalleryChoice? Choice { get; private set; }

    /// <summary>
    /// Raised when the host confirmed the choice: the dialog closes.
    /// </summary>
    public event EventHandler? Chosen;

    /// <inheritdoc />
    public void Dispose()
    {
        _listing.Cancel();
        _counting.Cancel();
    }

    /// <summary>
    /// Opens a nested folder.
    /// </summary>
    /// <param name="entry">The entry; pictures and videos are only shown.</param>
    [RelayCommand]
    private void OpenEntry(FolderEntryViewModel? entry)
    {
        if (entry is { IsFolder: true })
        {
            Visit(entry.Path);
        }
    }

    /// <summary>
    /// Opens a folder of the user or a disk.
    /// </summary>
    /// <param name="place">The place.</param>
    [RelayCommand]
    private void GoTo(FolderPlace? place)
    {
        if (place is not null)
        {
            Visit(place.Path);
        }
    }

    /// <summary>
    /// Opens the folder above the open one.
    /// </summary>
    [RelayCommand]
    private void Up()
    {
        if (Directory.GetParent(CurrentPath) is { } parent)
        {
            Visit(parent.FullName);
        }
    }

    /// <summary>
    /// Opens the folder typed into the address line.
    /// </summary>
    [RelayCommand]
    private void GoToPath() => Visit(PathText.Trim().Trim('"'));

    /// <summary>
    /// Returns to the folder open before, like Back of a file manager.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Step(_back, _forward);

    /// <summary>
    /// Goes again to the folder left with Back.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void Forward() => Step(_forward, _back);

    /// <summary>
    /// Checks whether there is a folder to return to.
    /// </summary>
    /// <returns><see langword="true"/> when there is.</returns>
    private bool CanGoBack() => _back.Count > 0;

    /// <summary>
    /// Checks whether there is a folder left with Back.
    /// </summary>
    /// <returns><see langword="true"/> when there is.</returns>
    private bool CanGoForward() => _forward.Count > 0;

    /// <summary>
    /// Opens another folder as a step of the user: the open one goes into the history of Back, and the history of
    /// Forward ends.
    /// </summary>
    /// <param name="path">The folder.</param>
    private void Visit(string path)
    {
        var left = CurrentPath;
        if (Open(path) && !string.Equals(left, CurrentPath, StringComparison.Ordinal))
        {
            _back.Push(left);
            _forward.Clear();
            UpdateHistory();
        }
    }

    /// <summary>
    /// Moves through the history: the open folder goes onto one stack, the folder from the other one is opened.
    /// A folder that cannot be opened any more is passed over.
    /// </summary>
    /// <param name="from">The stack to take the folder from.</param>
    /// <param name="to">The stack the open folder goes onto.</param>
    private void Step(Stack<string> from, Stack<string> to)
    {
        var left = CurrentPath;
        while (from.TryPop(out var target))
        {
            if (Open(target))
            {
                to.Push(left);
                break;
            }
        }

        UpdateHistory();
    }

    /// <summary>
    /// Tells the buttons of the history whether they can be used.
    /// </summary>
    private void UpdateHistory()
    {
        BackCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Confirms the open folder.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanShow))]
    private void Show()
    {
        if (Folder is { Files.Count: > 0 } folder)
        {
            Choice = new GalleryChoice(folder, StripMetadata);
            Chosen?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Checks whether the open folder has something to show.
    /// </summary>
    /// <returns><see langword="true"/> when it has.</returns>
    private bool CanShow() => !IsCounting && Folder is { Files.Count: > 0 };

    /// <summary>
    /// Counts again when the nested folders are taken in or left out.
    /// </summary>
    /// <param name="value">Whether the nested folders are shown.</param>
    partial void OnIncludeSubfoldersChanged(bool value) => _ = CountAsync();

    /// <summary>
    /// Rebuilds the rows when the width changes.
    /// </summary>
    /// <param name="value">The new number of entries in a row.</param>
    partial void OnColumnsChanged(int value) => Rows = Entries.Chunk(Math.Max(1, value)).ToArray();

    /// <summary>
    /// Rebuilds the rows when the entries change.
    /// </summary>
    /// <param name="value">The new entries.</param>
    partial void OnEntriesChanged(IReadOnlyList<FolderEntryViewModel> value) => Rows = value.Chunk(Math.Max(1, Columns)).ToArray();

    /// <summary>
    /// Lists the folders of the user and the disks.
    /// </summary>
    /// <returns>The places that exist.</returns>
    private static List<FolderPlace> BuildPlaces()
    {
        var places = new List<FolderPlace>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var (key, path) in new[]
        {
            ("Gallery.Browser.Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            ("Gallery.Browser.Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
            ("Gallery.Browser.Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            ("Gallery.Browser.Downloads", home.Length > 0 ? Path.Combine(home, "Downloads") : string.Empty),
            ("Gallery.Browser.Home", home),
        })
        {
            if (path.Length > 0 && Directory.Exists(path) && places.All(p => p.Path != path))
            {
                places.Add(new FolderPlace(Texts.Get(key), path));
            }
        }

        places.AddRange(FindDisks().Where(d => places.All(p => p.Path != d.Path)));
        return places;
    }

    /// <summary>
    /// Lists the disks: the drives of Windows, or the root and the mounted volumes elsewhere.
    /// </summary>
    /// <returns>The disks.</returns>
    private static IEnumerable<FolderPlace> FindDisks()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                string? label;
                try
                {
                    if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Network or DriveType.CDRom))
                    {
                        continue;
                    }

                    label = drive.VolumeLabel;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                var name = drive.Name.TrimEnd('\\');
                yield return new FolderPlace(string.IsNullOrWhiteSpace(label) ? name : $"{label} ({name})", drive.RootDirectory.FullName);
            }

            yield break;
        }

        yield return new FolderPlace("/", "/");
        var user = Environment.UserName;
        foreach (var mounts in new[] { "/Volumes", Path.Combine("/media", user), Path.Combine("/run/media", user), "/mnt" })
        {
            string[] volumes;
            try
            {
                volumes = Directory.Exists(mounts) ? Directory.GetDirectories(mounts) : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var volume in volumes)
            {
                yield return new FolderPlace(Path.GetFileName(volume), volume);
            }
        }
    }

    /// <summary>
    /// Opens a folder: lists it and counts what would be shown. A folder that cannot be read leaves the open one in
    /// place and says why.
    /// </summary>
    /// <param name="path">The folder.</param>
    /// <returns><see langword="true"/> when the folder was opened.</returns>
    private bool Open(string path)
    {
        List<FileSystemInfo> found;
        string full;
        try
        {
            full = Path.GetFullPath(path);
            found = new DirectoryInfo(full).EnumerateFileSystemInfos("*", GalleryFolder.WalkOptions).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            ErrorText = Texts.Format("Gallery.Browser.CannotOpen", ex.Message);
            PathText = CurrentPath;
            return false;
        }

        _listing.Cancel();
        _listing = new CancellationTokenSource();
        var listing = _listing.Token;
        ErrorText = string.Empty;
        CurrentPath = full;
        PathText = full;
        var folders = found.OfType<DirectoryInfo>()
            .OrderBy(d => d.Name, GalleryFolder.NaturalOrder)
            .Select(d => new FolderEntryViewModel(d.Name, d.FullName, FolderEntryKind.Folder, null));
        var files = found.OfType<FileInfo>()
            .Where(f => GalleryFolder.IsPicture(f.Name) || GalleryFolder.IsVideo(f.Name))
            .OrderBy(f => f.Name, GalleryFolder.NaturalOrder)
            .Select(f => GalleryFolder.IsVideo(f.Name)
                ? new FolderEntryViewModel(f.Name, f.FullName, FolderEntryKind.Video, null)
                : new FolderEntryViewModel(f.Name, f.FullName, FolderEntryKind.Picture, CanDecodeHere(f) ? e => _ = MakeThumbnailAsync(e, listing) : null));
        Entries = folders.Concat(files).Take(MAX_LISTED).ToArray();
        _ = CountAsync();
        return true;
    }

    /// <summary>
    /// Checks whether the small picture of a picture is made here: participants' formats only, of a sane size.
    /// </summary>
    /// <param name="file">The picture.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool CanDecodeHere(FileInfo file) => !GalleryFolder.NeedsDecoding(file.Name) && file.Length is > 0 and <= GalleryItem.MAX_LENGTH;

    /// <summary>
    /// Makes the small picture of a picture.
    /// </summary>
    /// <param name="entry">The entry of the picture.</param>
    /// <param name="cancellationToken">Cancelled when another folder is opened.</param>
    /// <returns>A task that completes when the small picture is in place or cannot be made.</returns>
    private async Task MakeThumbnailAsync(FolderEntryViewModel entry, CancellationToken cancellationToken)
    {
        try
        {
            await _thumbnailSlots.WaitAsync(cancellationToken);
            try
            {
                var image = await Task.Run(
                    async () => _decoder.Decode(await File.ReadAllBytesAsync(entry.Path, cancellationToken), THUMBNAIL_SIDE),
                    cancellationToken);
                if (!cancellationToken.IsCancellationRequested)
                {
                    entry.SetThumbnail(image);
                }
            }
            finally
            {
                _thumbnailSlots.Release();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            // The sign of a picture stays in place.
        }
    }

    /// <summary>
    /// Counts what of the open folder would be shown.
    /// </summary>
    /// <returns>A task that completes when the count is in place or a newer one started.</returns>
    private async Task CountAsync()
    {
        _counting.Cancel();
        _counting = new CancellationTokenSource();
        var token = _counting.Token;
        var path = CurrentPath;
        var subfolders = IncludeSubfolders;
        IsCounting = true;
        Folder = null;
        SummaryText = Texts.Get("Gallery.Browser.Counting");
        NotesText = string.Empty;
        try
        {
            var folder = await Task.Run(() => GalleryFolder.Describe(path, subfolders, _canDecode, token), token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            Folder = folder;
            SummaryText = folder.Files.Count == 0
                ? Texts.Get("Gallery.Browser.Nothing")
                : Texts.Format("Gallery.Confirm.Summary", folder.Pictures, folder.Videos, DisplayFormat.Size(folder.TotalBytes));
            NotesText = DescribeLeftOut(folder);
            IsCounting = false;
        }
        catch (OperationCanceledException)
        {
            // A newer count took over.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!token.IsCancellationRequested)
            {
                SummaryText = Texts.Format("Gallery.Browser.CannotOpen", ex.Message);
                IsCounting = false;
            }
        }
    }

    /// <summary>
    /// Words what of a folder is not shown.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <returns>One line for each kind of file left out, or an empty text.</returns>
    private static string DescribeLeftOut(GalleryFolder folder)
    {
        var lines = new List<string>();
        if (folder.Unsupported > 0)
        {
            lines.Add(Texts.Format("Gallery.Confirm.Unsupported", folder.Unsupported));
        }

        if (folder.Truncated)
        {
            lines.Add(Texts.Format("Gallery.Confirm.Truncated", GalleryItem.MAX_ITEMS));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
