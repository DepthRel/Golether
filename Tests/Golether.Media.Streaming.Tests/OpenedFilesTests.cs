using Golether.Core.Enums;
using Golether.Media.Streaming.Files;
using Golether.Media.Streaming.Gallery;

namespace Golether.Media.Streaming.Tests;

/// <summary>
/// Files the system opened for the application (a film or a folder chosen on Android): they are named by a path of
/// their own and read through the opener, by the film, the shown folder and its pictures alike.
/// </summary>
public sealed class OpenedFilesTests : IDisposable
{
    /// <summary>
    /// A minimal JPEG: the start, a scan and the end.
    /// </summary>
    private static readonly byte[] _jpeg = [0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x04, 0x00, 0x00, 0x12, 0x34, 0xFF, 0xD9];

    /// <summary>
    /// The folder where showings keep the files they make.
    /// </summary>
    private readonly string _work = Directory.CreateTempSubdirectory("golether-opened-work-").FullName;

    /// <inheritdoc />
    public void Dispose()
    {
        OpenedFiles.Clear();
        Directory.Delete(_work, recursive: true);
    }

    /// <summary>
    /// The path ends with the name of the file, so the film is announced by its name, and its bytes come from the opener.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Add_NamesTheFileAndReadsItThroughTheOpener()
    {
        byte[] bytes = [1, 2, 3, 4, 5];
        var path = OpenedFiles.Add("Film night.mkv", () => new MemoryStream(bytes, writable: false));

        var descriptor = await MediaFiles.DescribeAsync(path, TestContext.Current.CancellationToken);
        await using var stream = MediaFiles.OpenRead(path);
        var read = new byte[bytes.Length];
        await stream.ReadExactlyAsync(read, TestContext.Current.CancellationToken);

        Assert.Equal("Film night.mkv", Path.GetFileName(path));
        Assert.True(MediaFiles.Exists(path));
        Assert.False(File.Exists(path));
        Assert.Equal("Film night.mkv", descriptor.FileName);
        Assert.Equal(bytes.Length, descriptor.Length);
        Assert.Equal(bytes, read);
    }

    /// <summary>
    /// A name the system gave cannot lead the path anywhere else: separators and a name of dots are taken out.
    /// </summary>
    [Fact]
    public void Add_KeepsTheNameInsideItsFolder()
    {
        var nested = OpenedFiles.Add("../../secret/a.jpg", () => new MemoryStream(_jpeg));
        var dots = OpenedFiles.Add("..", () => new MemoryStream(_jpeg));

        Assert.Equal(Path.GetDirectoryName(Path.GetDirectoryName(nested)), Path.GetDirectoryName(Path.GetDirectoryName(dots)));
        Assert.DoesNotContain("..", Path.GetFileName(dots), StringComparison.Ordinal);
        Assert.True(MediaFiles.Exists(nested));
    }

    /// <summary>
    /// Once the session ends, its files are forgotten and no longer open.
    /// </summary>
    [Fact]
    public void Clear_ForgetsTheFiles()
    {
        var path = OpenedFiles.Add("a.jpg", () => new MemoryStream(_jpeg));

        OpenedFiles.Clear();

        Assert.False(MediaFiles.Exists(path));
        Assert.Throws<DirectoryNotFoundException>(() => MediaFiles.OpenRead(path).Dispose());
    }

    /// <summary>
    /// A folder that does not lie on the disk is walked like one that does, and its pictures are read through the
    /// system when they are shown.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task FolderBehindTheSystem_IsDescribedAndShown()
    {
        var token = TestContext.Current.CancellationToken;
        var root = new Folder(
            "Camera",
            Picture("IMG_10.jpg"),
            Picture("IMG_2.jpg"),
            new GalleryEntry("clip.mp4", OpenedFiles.Add("clip.mp4", () => new MemoryStream([1, 2])), 2, null),
            new GalleryEntry("camera.dng", string.Empty, 9, null),
            new GalleryEntry("Trip", string.Empty, 0, new Folder("Trip", Picture("x.jpg"))));

        var folder = GalleryFolder.Describe(root, canDecode: false, cancellationToken: token);
        using var catalog = await GalleryCatalog.CreateAsync(folder, new GalleryOptions { StripMetadata = true, WorkDirectory = _work }, null, token);
        await using var shown = catalog.OpenRead(1);
        using var copy = new MemoryStream();
        await shown.CopyToAsync(copy, token);

        Assert.Equal("Camera", folder.Title);
        Assert.Equal(["clip.mp4", "IMG_2.jpg", "IMG_10.jpg", "x.jpg"], folder.Files.Select(f => Path.GetFileName(f.Path)));
        Assert.Equal(["", "", "", "Trip"], folder.Files.Select(f => f.Folder));
        Assert.Equal(GalleryItemKind.Video, folder.Files[0].Kind);
        Assert.Equal(1, folder.Unsupported);
        Assert.Equal(4, catalog.Items.Count);
        Assert.Equal(_jpeg, copy.ToArray());
    }

    /// <summary>
    /// Makes an entry of a picture the system opened.
    /// </summary>
    /// <param name="name">The name of the picture.</param>
    /// <returns>The entry.</returns>
    private static GalleryEntry Picture(string name)
        => new(name, OpenedFiles.Add(name, () => new MemoryStream(_jpeg, writable: false)), _jpeg.Length, null);

    /// <summary>
    /// A folder that lies behind the system.
    /// </summary>
    /// <param name="name">The name of the folder.</param>
    /// <param name="entries">What it holds.</param>
    private sealed class Folder(string name, params GalleryEntry[] entries) : IGalleryDirectory
    {
        /// <inheritdoc />
        public string Name => name;

        /// <inheritdoc />
        public IReadOnlyList<GalleryEntry> List() => entries;
    }
}
