using System.Text;
using Golether.Core.Enums;
using Golether.Media.Streaming.Gallery;
using Golether.Media.Streaming.Protocol;

namespace Golether.Media.Streaming.Tests;

/// <summary>
/// Tests of a shown folder: what is offered of it, in which order, and how its pictures are found by their keys.
/// </summary>
public sealed class GalleryTests : IDisposable
{
    /// <summary>
    /// A minimal JPEG: the start, a scan and the end.
    /// </summary>
    private static readonly byte[] _jpeg = [0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x04, 0x00, 0x00, 0x12, 0x34, 0xFF, 0xD9];

    /// <summary>
    /// The temporary folder.
    /// </summary>
    private readonly string _folder = Directory.CreateTempSubdirectory("golether-gallery-").FullName;

    /// <summary>
    /// The folder where showings keep the files they make.
    /// </summary>
    private readonly string _work = Directory.CreateTempSubdirectory("golether-gallery-work-").FullName;

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
        Directory.Delete(_work, recursive: true);
    }

    /// <summary>
    /// The files of a folder come first in the order a person expects, then its nested folders one after another;
    /// RAW files are counted as left out, other files are ignored.
    /// </summary>
    [Fact]
    public void Describe_SortsNaturallyAndWalksNestedFolders()
    {
        Write("IMG_10.jpg", _jpeg);
        Write("IMG_2.jpg", _jpeg);
        Write("b.png", _jpeg);
        Write("clip.mp4", [1]);
        Write("phone.HEIC", [1]);
        Write("camera.CR2", [1]);
        Write("notes.txt", [1]);
        Write("Day 10/x.jpg", _jpeg);
        Write("Day 2/y.jpg", _jpeg);
        Write("Day 2/Deep/z.png", _jpeg);

        var folder = GalleryFolder.Describe(_folder, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            ["b.png", "clip.mp4", "IMG_2.jpg", "IMG_10.jpg", "phone.HEIC", "y.jpg", "z.png", "x.jpg"],
            folder.Files.Select(f => Path.GetFileName(f.Path)));
        Assert.Equal(["", "", "", "", "", "Day 2", "Day 2/Deep", "Day 10"], folder.Files.Select(f => f.Folder));
        Assert.Equal(GalleryItemKind.Video, folder.Files[1].Kind);
        Assert.True(folder.Files[4].NeedsDecoding);
        Assert.Equal(7, folder.Pictures);
        Assert.Equal(1, folder.Videos);
        Assert.Equal(1, folder.Unsupported);
        Assert.Equal((6 * _jpeg.Length) + 1, folder.TotalBytes);
        Assert.False(folder.Truncated);
        Assert.Equal(Path.GetFileName(_folder), folder.Title);
    }

    /// <summary>
    /// Without the nested folders only the folder itself is shown; a device that cannot decode HEIC leaves it out.
    /// </summary>
    [Fact]
    public void Describe_KeepsToTheFolderAndToWhatCanBeDecoded()
    {
        Write("a.jpg", _jpeg);
        Write("phone.heic", [1]);
        Write("Nested/b.jpg", _jpeg);

        var folder = GalleryFolder.Describe(_folder, includeSubfolders: false, canDecode: false, TestContext.Current.CancellationToken);

        Assert.Equal(["a.jpg"], folder.Files.Select(f => Path.GetFileName(f.Path)));
        Assert.Equal(1, folder.Unsupported);
    }

    /// <summary>
    /// A single picture or video is a showing of one; a single file of another kind shows nothing.
    /// </summary>
    [Fact]
    public void Describe_TakesASingleFile()
    {
        Write("one.jpg", _jpeg);
        Write("film.mkv", [1]);
        Write("notes.txt", [1]);

        Assert.Equal(GalleryItemKind.Picture, Assert.Single(GalleryFolder.Describe(Path.Combine(_folder, "one.jpg"), cancellationToken: TestContext.Current.CancellationToken).Files).Kind);
        Assert.Equal(GalleryItemKind.Video, Assert.Single(GalleryFolder.Describe(Path.Combine(_folder, "film.mkv"), cancellationToken: TestContext.Current.CancellationToken).Files).Kind);
        Assert.Empty(GalleryFolder.Describe(Path.Combine(_folder, "notes.txt"), cancellationToken: TestContext.Current.CancellationToken).Files);
    }

    /// <summary>
    /// The catalog leaves out a file that only looks like a picture, gives every picture its own random key and
    /// finds the bytes by it; a key of nothing shown is not found, and a video is listed but never sent as a picture.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Catalog_FindsPicturesByTheirKeys()
    {
        var token = TestContext.Current.CancellationToken;
        Write("a.jpg", _jpeg);
        Write("b.jpg", _jpeg);
        Write("c.mp4", [1, 2, 3]);
        Write("fake.jpg", Encoding.ASCII.GetBytes("not a picture at all"));

        using var catalog = await GalleryCatalog.CreateAsync(GalleryFolder.Describe(_folder, cancellationToken: token), Options(null), null, token);

        Assert.Equal(["a.jpg", "b.jpg", "c.mp4"], catalog.Items.Select(i => i.FileName));
        Assert.Equal(GalleryItemKind.Video, catalog.Items[2].Kind);
        Assert.Equal(3, catalog.Items[2].Length);
        Assert.All(catalog.Items, i => Assert.True(i.IsValid()));
        Assert.NotEqual(catalog.Items[0].Key, catalog.Items[1].Key);
        Assert.True(catalog.Id.Length == 32 && catalog.Id.All(char.IsAsciiHexDigit));

        var key = ChunkProtocol.GetMediaKey(catalog.Items[1].ToDescriptor());
        Assert.True(catalog.TryFind(key, out var descriptor, out var open));
        Assert.Equal(catalog.Items[1].Length, descriptor.Length);
        using (var stream = open())
        {
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            Assert.Equal(_jpeg, bytes);
        }

        Assert.False(catalog.TryFind(key ^ 1, out _, out _));
        Assert.False(catalog.TryFind(ChunkProtocol.GetMediaKey(catalog.Items[2].ToDescriptor()), out _, out _));
        Assert.Throws<InvalidOperationException>(() => catalog.OpenRead(2));
        Assert.Null(await catalog.OpenPreviewAsync(2, token));

        // A second showing of the same folder has other keys: an old key does not reach a picture any more.
        using var again = await GalleryCatalog.CreateAsync(GalleryFolder.Describe(_folder, cancellationToken: token), Options(null), null, token);
        Assert.False(again.TryFind(key, out _, out _));
        Assert.NotEqual(catalog.Id, again.Id);
    }

    /// <summary>
    /// A HEIC picture is turned into a JPEG kept in the folder of the showing, a video gets a frame for its small
    /// picture, the progress counts every file, and the end of the showing deletes what it made.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Catalog_DecodesWhatParticipantsCannotAndCleansUp()
    {
        var token = TestContext.Current.CancellationToken;
        Write("phone.heic", [1, 2, 3, 4]);
        Write("clip.mov", [1]);
        var frames = new FakeFrames(_jpeg);
        var reports = new List<int>();

        var catalog = await GalleryCatalog.CreateAsync(GalleryFolder.Describe(_folder, cancellationToken: token), Options(frames), new SyncProgress(reports.Add), token);

        Assert.Equal(["clip.mov", "phone.heic"], catalog.Items.Select(i => i.FileName));
        Assert.Equal(_jpeg.Length, catalog.Items[1].Length);
        using (var picture = catalog.OpenRead(1))
        {
            var bytes = new byte[picture.Length];
            picture.ReadExactly(bytes);
            Assert.Equal(_jpeg, bytes);
        }

        await using (var preview = await catalog.OpenPreviewAsync(0, token))
        {
            Assert.NotNull(preview);
        }

        Assert.Equal([(0.0, 8192), (0.1, 640)], frames.Requests.Order());
        Assert.Equal(2, reports.Max());
        Assert.Single(Directory.GetDirectories(_work));

        catalog.Dispose();
        Assert.Empty(Directory.GetDirectories(_work));
    }

    /// <summary>
    /// Makes the options of a showing.
    /// </summary>
    /// <param name="frames">The decoder of frames, or <see langword="null"/>.</param>
    /// <returns>The options.</returns>
    private GalleryOptions Options(IFrameExtractor? frames) => new() { StripMetadata = true, Frames = frames, WorkDirectory = _work };

    /// <summary>
    /// Writes a file into the folder, with the nested folders of its name.
    /// </summary>
    /// <param name="name">The file name, relative to the folder.</param>
    /// <param name="content">The content.</param>
    private void Write(string name, byte[] content)
    {
        var path = Path.Combine(_folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    /// <summary>
    /// A decoder of frames that writes the same JPEG for every request and notes the requests.
    /// </summary>
    /// <param name="jpeg">The JPEG written.</param>
    private sealed class FakeFrames(byte[] jpeg) : IFrameExtractor
    {
        /// <summary>
        /// Gets the requests: where the frame was taken and its longest side.
        /// </summary>
        public List<(double Position, int MaxSide)> Requests { get; } = [];

        /// <inheritdoc />
        public bool IsAvailable => true;

        /// <inheritdoc />
        public async Task<bool> ExtractAsync(string source, string target, double position, int maxSide, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add((position, maxSide));
            }

            await File.WriteAllBytesAsync(target, jpeg, cancellationToken);
            return true;
        }
    }

    /// <summary>
    /// Reports progress at once on the reporting thread, so the test sees every report.
    /// </summary>
    /// <param name="report">Takes a report.</param>
    private sealed class SyncProgress(Action<int> report) : IProgress<int>
    {
        /// <summary>
        /// Keeps reports of parallel preparations apart.
        /// </summary>
        private readonly Lock _gate = new();

        /// <inheritdoc />
        public void Report(int value)
        {
            lock (_gate)
            {
                report(value);
            }
        }
    }
}
