using System.Globalization;
using Golether.Core.Enums;
using Golether.Core.Media;

namespace Golether.Media.Streaming.Gallery;

/// <summary>
/// What a folder chosen by the host holds: the pictures and videos that can be shown and what is left out. Only names
/// and sizes are looked at, so the host sees it at once and decides before any file is read.
/// </summary>
/// <param name="Title">The name of the folder (or of the single file).</param>
/// <param name="Files">The files shown, in the order they are shown: those of a folder first, then its nested
/// folders one after another, each in the same way.</param>
/// <param name="Unsupported">The number of pictures that cannot be shown (RAW, too large, or of a format this device
/// cannot decode).</param>
/// <param name="TotalBytes">The size of the pictures together: what participants receive at most.</param>
/// <param name="Truncated">Whether the folder holds more than one showing takes.</param>
public sealed record GalleryFolder(string Title, IReadOnlyList<GalleryFile> Files, int Unsupported, long TotalBytes, bool Truncated)
{
    /// <summary>
    /// How deep nested folders are looked into.
    /// </summary>
    public const int MAX_DEPTH = 16;

    /// <summary>
    /// The most files looked at: a whole disk chosen by mistake is not walked through to the end.
    /// </summary>
    public const int MAX_SCANNED = 100_000;

    /// <summary>
    /// The extensions of the pictures participants show as they are.
    /// </summary>
    private static readonly HashSet<string> _pictures = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".jfif", ".png", ".webp", ".gif", ".bmp" };

    /// <summary>
    /// The extensions of the pictures the host turns into JPEG first, because participants cannot decode them.
    /// </summary>
    private static readonly HashSet<string> _decoded = new(StringComparer.OrdinalIgnoreCase) { ".heic", ".heif", ".hif", ".avif", ".tif", ".tiff" };

    /// <summary>
    /// The extensions of camera RAW files, which are not shown.
    /// </summary>
    private static readonly HashSet<string> _raw = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dng", ".cr2", ".cr3", ".crw", ".nef", ".nrw", ".arw", ".srf", ".sr2", ".raf", ".orf", ".rw2", ".pef", ".srw", ".x3f",
    };

    /// <summary>
    /// The extensions of the videos.
    /// </summary>
    private static readonly HashSet<string> _videos = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".webm", ".ts", ".m2ts", ".mts", ".wmv", ".flv", ".mpg", ".mpeg", ".3gp",
    };

    /// <summary>
    /// What is skipped while walking a folder: hidden and system files, and links, so what is shown is what the host
    /// sees in the folder, not something a link leads to (and a link back up cannot make the walk endless).
    /// </summary>
    private static readonly EnumerationOptions _walk = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
    };

    /// <summary>
    /// Puts names in the order a person expects: "IMG_2" before "IMG_10".
    /// </summary>
    private static readonly StringComparer _naturalOrder = StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);

    /// <summary>
    /// Gets the number of pictures shown.
    /// </summary>
    public int Pictures => Files.Count(f => f.Kind == GalleryItemKind.Picture);

    /// <summary>
    /// Gets the number of videos shown.
    /// </summary>
    public int Videos => Files.Count(f => f.Kind == GalleryItemKind.Video);

    /// <summary>
    /// Gets the order of names a person expects, shared with the views that list folders.
    /// </summary>
    public static StringComparer NaturalOrder => _naturalOrder;

    /// <summary>
    /// Gets the options of walking a folder, for the views that list folders the same way; a new copy every time.
    /// </summary>
    public static EnumerationOptions WalkOptions => new()
    {
        RecurseSubdirectories = _walk.RecurseSubdirectories,
        IgnoreInaccessible = _walk.IgnoreInaccessible,
        AttributesToSkip = _walk.AttributesToSkip,
    };

    /// <summary>
    /// Checks whether a file is a picture by its name, whether it is shown as it is or decoded first.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns><see langword="true"/> for a picture.</returns>
    public static bool IsPicture(string path) => _pictures.Contains(Path.GetExtension(path)) || NeedsDecoding(path);

    /// <summary>
    /// Checks whether a picture is of a format the host turns into JPEG before it is shown.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns><see langword="true"/> for HEIC, AVIF and TIFF.</returns>
    public static bool NeedsDecoding(string path) => _decoded.Contains(Path.GetExtension(path));

    /// <summary>
    /// Checks whether a file is a video by its name.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns><see langword="true"/> for a video.</returns>
    public static bool IsVideo(string path) => _videos.Contains(Path.GetExtension(path));

    /// <summary>
    /// Looks at a folder with its nested folders, or at a single file.
    /// </summary>
    /// <param name="path">The folder, or a file shown alone.</param>
    /// <param name="includeSubfolders">Whether the nested folders are shown too.</param>
    /// <param name="canDecode">Whether this device can turn HEIC, AVIF and TIFF into JPEG; without it they are left out.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What can be shown.</returns>
    /// <exception cref="DirectoryNotFoundException">There is no such folder or file.</exception>
    public static GalleryFolder Describe(string path, bool includeSubfolders = true, bool canDecode = true, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        var walk = new Walk(includeSubfolders, canDecode, cancellationToken);
        if (File.Exists(full))
        {
            walk.Take(new FileInfo(full), string.Empty);
            return walk.Result(Path.GetFileName(full));
        }

        var folder = new DirectoryInfo(full);
        if (!folder.Exists)
        {
            throw new DirectoryNotFoundException(full);
        }

        walk.Visit(folder, string.Empty, 0);
        return walk.Result(folder.Name.Length > 0 ? folder.Name : full);
    }

    /// <summary>
    /// One walk through a folder: what was found so far.
    /// </summary>
    private sealed class Walk
    {
        /// <summary>
        /// Whether the nested folders are walked into.
        /// </summary>
        private readonly bool _includeSubfolders;

        /// <summary>
        /// Whether HEIC, AVIF and TIFF can be decoded.
        /// </summary>
        private readonly bool _canDecode;

        /// <summary>
        /// Stops the walk.
        /// </summary>
        private readonly CancellationToken _cancellationToken;

        /// <summary>
        /// The files shown, in order.
        /// </summary>
        private readonly List<GalleryFile> _files = [];

        /// <summary>
        /// The number of pictures left out.
        /// </summary>
        private int _unsupported;

        /// <summary>
        /// The number of files looked at.
        /// </summary>
        private int _scanned;

        /// <summary>
        /// Whether the walk stopped before the end.
        /// </summary>
        private bool _stopped;

        /// <summary>
        /// Initializes a new instance of the <see cref="Walk"/> class.
        /// </summary>
        /// <param name="includeSubfolders">Whether the nested folders are walked into.</param>
        /// <param name="canDecode">Whether HEIC, AVIF and TIFF can be decoded.</param>
        /// <param name="cancellationToken">Stops the walk.</param>
        public Walk(bool includeSubfolders, bool canDecode, CancellationToken cancellationToken)
        {
            _includeSubfolders = includeSubfolders;
            _canDecode = canDecode;
            _cancellationToken = cancellationToken;
        }

        /// <summary>
        /// Walks a folder: its files first, then its nested folders.
        /// </summary>
        /// <param name="folder">The folder.</param>
        /// <param name="relative">Its path relative to the shown folder.</param>
        /// <param name="depth">How deep it lies.</param>
        public void Visit(DirectoryInfo folder, string relative, int depth)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            List<FileSystemInfo> entries;
            try
            {
                entries = folder.EnumerateFileSystemInfos("*", _walk).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // A folder that cannot be read is skipped like a hidden one.
                return;
            }

            foreach (var file in entries.OfType<FileInfo>().OrderBy(f => f.Name, _naturalOrder))
            {
                if (_stopped || ++_scanned > MAX_SCANNED)
                {
                    _stopped = true;
                    return;
                }

                Take(file, relative);
            }

            if (!_includeSubfolders || depth >= MAX_DEPTH)
            {
                return;
            }

            foreach (var nested in entries.OfType<DirectoryInfo>().OrderBy(d => d.Name, _naturalOrder))
            {
                if (_stopped)
                {
                    return;
                }

                Visit(nested, relative.Length == 0 ? nested.Name : relative + GalleryItem.FOLDER_SEPARATOR + nested.Name, depth + 1);
            }
        }

        /// <summary>
        /// Sorts a file into what is shown and what is left out.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="relative">The folder it lies in, relative to the shown folder.</param>
        public void Take(FileInfo file, string relative)
        {
            long length;
            try
            {
                length = file.Length;
            }
            catch (IOException)
            {
                return;
            }

            var folder = GalleryItem.NormalizeFolder(relative);
            if (IsVideo(file.Name))
            {
                if (length > 0)
                {
                    _files.Add(new GalleryFile(file.FullName, folder, GalleryItemKind.Video, length, false));
                }

                return;
            }

            var decoded = NeedsDecoding(file.Name);
            if (_pictures.Contains(file.Extension) || decoded)
            {
                if (length is > 0 and <= GalleryItem.MAX_LENGTH && (!decoded || _canDecode))
                {
                    _files.Add(new GalleryFile(file.FullName, folder, GalleryItemKind.Picture, length, decoded));
                }
                else
                {
                    _unsupported++;
                }
            }
            else if (_raw.Contains(file.Extension))
            {
                _unsupported++;
            }
        }

        /// <summary>
        /// Returns what was found.
        /// </summary>
        /// <param name="title">The title of the showing.</param>
        /// <returns>The folder.</returns>
        public GalleryFolder Result(string title)
        {
            var shown = _files.Take(GalleryItem.MAX_ITEMS).ToArray();
            return new GalleryFolder(
                title,
                shown,
                _unsupported,
                shown.Where(f => f.Kind == GalleryItemKind.Picture).Sum(f => f.Length),
                _stopped || _files.Count > shown.Length);
        }
    }
}
