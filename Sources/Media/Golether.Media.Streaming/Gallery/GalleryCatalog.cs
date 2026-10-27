using System.Collections.Concurrent;
using System.Security.Cryptography;
using Golether.Core.Enums;
using Golether.Core.Media;
using Golether.Media.Streaming.Files;
using Golether.Media.Streaming.Protocol;

namespace Golether.Media.Streaming.Gallery;

/// <summary>
/// The pictures and videos the host shows: their list as participants see it and the files behind it. Every showing
/// gets new random keys, so a key says nothing about the file and is useless once the host shows something else.
/// Pictures participants cannot decode (HEIC, AVIF, TIFF) are turned into JPEG files kept in a folder of the
/// showing, deleted when it ends.
/// </summary>
public sealed class GalleryCatalog : ISharedFileSet, IDisposable
{
    /// <summary>
    /// The most files prepared at once.
    /// </summary>
    private const int PREPARATION_PARALLELISM = 4;

    /// <summary>
    /// The longest side of a decoded picture: larger ones are made smaller, so the JPEG stays within
    /// <see cref="GalleryItem.MAX_LENGTH"/>.
    /// </summary>
    private const int DECODED_SIDE = 8192;

    /// <summary>
    /// The longest side of the frame a small picture of a video is made from.
    /// </summary>
    private const int PREVIEW_SIDE = 640;

    /// <summary>
    /// Where in a video its small picture is taken: past the titles that often open it.
    /// </summary>
    private const double PREVIEW_POSITION = 0.1;

    /// <summary>
    /// The files behind the items, in the same order.
    /// </summary>
    private readonly IReadOnlyList<Entry> _entries;

    /// <summary>
    /// The pictures by media key.
    /// </summary>
    private readonly Dictionary<ulong, Entry> _byKey;

    /// <summary>
    /// The folder of the JPEG files this showing makes.
    /// </summary>
    private readonly string _directory;

    /// <summary>
    /// The decoder of video frames, or <see langword="null"/>.
    /// </summary>
    private readonly IFrameExtractor? _frames;

    /// <summary>
    /// The frames of the videos being made or made, by index.
    /// </summary>
    private readonly ConcurrentDictionary<int, Lazy<Task<string?>>> _previews = new();

    /// <summary>
    /// Cancelled when the showing ends: frames still being made are of no use.
    /// </summary>
    private readonly CancellationTokenSource _lifetime = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="GalleryCatalog"/> class.
    /// </summary>
    /// <param name="id">The identifier of the showing.</param>
    /// <param name="title">The title.</param>
    /// <param name="entries">The files.</param>
    /// <param name="directory">The folder of the JPEG files of the showing.</param>
    /// <param name="frames">The decoder of video frames.</param>
    private GalleryCatalog(string id, string title, IReadOnlyList<Entry> entries, string directory, IFrameExtractor? frames)
    {
        Id = id;
        Title = MediaDescriptor.NormalizeFileName(title);
        _entries = entries;
        _directory = directory;
        _frames = frames;
        Items = entries.Select(e => e.Item).ToArray();
        _byKey = entries.Where(e => e.Layout is not null).ToDictionary(e => ChunkProtocol.GetMediaKey(e.Descriptor));
    }

    /// <summary>
    /// Gets the identifier of this showing: 32 hexadecimal digits.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the title shown to participants: the name of the folder.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the items as participants see them.
    /// </summary>
    public IReadOnlyList<GalleryItem> Items { get; }

    /// <summary>
    /// Prepares the files of a folder: reads the headers of the pictures, leaves their metadata out when asked to,
    /// and turns the pictures participants cannot decode into JPEG.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <param name="options">How the files are prepared.</param>
    /// <param name="progress">Told the number of files prepared so far.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The catalog; files that cannot be read are left out.</returns>
    public static async Task<GalleryCatalog> CreateAsync(GalleryFolder folder, GalleryOptions options, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(options);
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var directory = Path.Combine(Path.GetFullPath(options.WorkDirectory), id);
        var files = folder.Files.Take(GalleryItem.MAX_ITEMS).ToArray();
        var entries = new Entry?[files.Length];
        var done = 0;

        try
        {
            await Parallel.ForEachAsync(
                Enumerable.Range(0, files.Length),
                new ParallelOptions { MaxDegreeOfParallelism = PREPARATION_PARALLELISM, CancellationToken = cancellationToken },
                async (index, token) =>
                {
                    entries[index] = await TryPrepareAsync(files[index], index, directory, options, token).ConfigureAwait(false);
                    progress?.Report(Interlocked.Increment(ref done));
                }).ConfigureAwait(false);
        }
        catch
        {
            DeleteDirectory(directory);
            throw;
        }

        return new GalleryCatalog(id, folder.Title, entries.OfType<Entry>().ToArray(), directory, options.Frames);
    }

    /// <summary>
    /// Deletes the folders showings left behind, for example when the application was closed during one.
    /// </summary>
    /// <param name="workDirectory">The folder where showings keep their files.</param>
    public static void DeleteLeftovers(string workDirectory)
    {
        if (!Directory.Exists(workDirectory))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(workDirectory))
        {
            DeleteDirectory(directory);
        }
    }

    /// <summary>
    /// Returns the file of an item as the host has it.
    /// </summary>
    /// <param name="index">The index of the item.</param>
    /// <returns>The full path.</returns>
    public string PathOf(int index) => _entries[index].Source;

    /// <summary>
    /// Opens the bytes of a picture as participants receive them.
    /// </summary>
    /// <param name="index">The index of the picture.</param>
    /// <returns>A readable, seekable stream.</returns>
    /// <exception cref="InvalidOperationException">The item is a video.</exception>
    public Stream OpenRead(int index)
    {
        var entry = _entries[index];
        return entry.Layout is { } layout ? layout.Open(entry.Served) : throw new InvalidOperationException("A video is not sent as a picture.");
    }

    /// <summary>
    /// Opens what the small picture of an item is made from: the picture itself, or a frame of a video.
    /// </summary>
    /// <param name="index">The index of the item.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A readable stream, or <see langword="null"/> when a frame of the video cannot be made.</returns>
    public async Task<Stream?> OpenPreviewAsync(int index, CancellationToken cancellationToken)
    {
        var entry = _entries[index];
        if (entry.Layout is not null)
        {
            return OpenRead(index);
        }

        if (_frames is null)
        {
            return null;
        }

        var frame = await _previews.GetOrAdd(index, i => new Lazy<Task<string?>>(() => MakePreviewAsync(i))).Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return frame is null ? null : MediaFiles.OpenRead(frame);
    }

    /// <inheritdoc />
    public bool TryFind(ulong key, out MediaDescriptor descriptor, out Func<Stream> open)
    {
        if (_byKey.TryGetValue(key, out var entry))
        {
            descriptor = entry.Descriptor;
            open = () => entry.Layout!.Open(entry.Served);
            return true;
        }

        descriptor = null!;
        open = null!;
        return false;
    }

    /// <summary>
    /// Ends the showing: frames still being made are dropped and the JPEG files it made are deleted.
    /// </summary>
    public void Dispose()
    {
        _lifetime.Cancel();
        DeleteDirectory(_directory);
    }

    /// <summary>
    /// Prepares one file.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="index">Its place in the folder, which names the JPEG made of it.</param>
    /// <param name="directory">The folder of the JPEG files of the showing.</param>
    /// <param name="options">How the files are prepared.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entry, or <see langword="null"/> when the file cannot be shown.</returns>
    private static async Task<Entry?> TryPrepareAsync(GalleryFile file, int index, string directory, GalleryOptions options, CancellationToken cancellationToken)
    {
        var name = MediaDescriptor.NormalizeFileName(Path.GetFileName(file.Path));
        if (file.Kind == GalleryItemKind.Video)
        {
            return File.Exists(file.Path) ? Make(file, file.Path, null, file.Length, name) : null;
        }

        var served = file.Path;
        if (file.NeedsDecoding)
        {
            if (options.Frames is not { IsAvailable: true } frames)
            {
                return null;
            }

            Directory.CreateDirectory(directory);
            served = Path.Combine(directory, index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".jpg");
            if (!await frames.ExtractAsync(file.Path, served, 0, DECODED_SIDE, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }
        }

        var layout = await Task.Run(() => TryRead(served, options.StripMetadata), cancellationToken).ConfigureAwait(false);
        return layout is { Length: > 0 and <= GalleryItem.MAX_LENGTH } ? Make(file, served, layout, layout.Length, name) : null;
    }

    /// <summary>
    /// Reads the header of a picture.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="stripMetadata">Whether the metadata is left out.</param>
    /// <returns>The layout, or <see langword="null"/> when the file is not a readable picture.</returns>
    private static PictureLayout? TryRead(string path, bool stripMetadata)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096);
            return PictureLayout.Read(file, stripMetadata);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Makes the entry of a prepared file under a new random key.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="served">The file participants receive: the picture itself or the JPEG made of it.</param>
    /// <param name="layout">The bytes participants receive, or <see langword="null"/> for a video.</param>
    /// <param name="length">The length shown to participants.</param>
    /// <param name="name">The file name shown to participants.</param>
    /// <returns>The entry.</returns>
    private static Entry Make(GalleryFile file, string served, PictureLayout? layout, long length, string name)
    {
        // The key is random: its first eight bytes, the media key of a request, do not meet those of the film or of
        // another picture in practice.
        var key = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var item = new GalleryItem(name, length, key) { Kind = file.Kind, Folder = file.Folder };
        return new Entry(file.Path, served, layout, item, item.ToDescriptor());
    }

    /// <summary>
    /// Deletes a folder of a showing; a file still open is left for the next start.
    /// </summary>
    /// <param name="directory">The folder.</param>
    private static void DeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Deleted with the leftovers next time.
        }
    }

    /// <summary>
    /// Makes the frame of a video its small picture is made from.
    /// </summary>
    /// <param name="index">The index of the video.</param>
    /// <returns>The JPEG file, or <see langword="null"/> when it cannot be made.</returns>
    private async Task<string?> MakePreviewAsync(int index)
    {
        var target = Path.Combine(_directory, "preview-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".jpg");
        try
        {
            Directory.CreateDirectory(_directory);
            return await _frames!.ExtractAsync(_entries[index].Source, target, PREVIEW_POSITION, PREVIEW_SIDE, _lifetime.Token).ConfigureAwait(false)
                ? target
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// An item of the catalog.
    /// </summary>
    /// <param name="Source">The file in the shown folder.</param>
    /// <param name="Served">The file participants receive: the picture itself or the JPEG made of it.</param>
    /// <param name="Layout">The bytes participants receive, or <see langword="null"/> for a video.</param>
    /// <param name="Item">The item participants see.</param>
    /// <param name="Descriptor">The descriptor a picture is fetched by.</param>
    private sealed record Entry(string Source, string Served, PictureLayout? Layout, GalleryItem Item, MediaDescriptor Descriptor);
}
