using Golether.Localization;
using Golether.Mobile.Services;

namespace Golether.Mobile.Tests;

/// <summary>
/// Tests of <see cref="ImportedTrackFiles"/>: a chosen file is copied where the player can read it, whatever its name
/// and size claim.
/// </summary>
public sealed class ImportedTrackFilesTests : IDisposable
{
    /// <summary>
    /// The limit the tests pass.
    /// </summary>
    private const long LIMIT = 16;

    /// <summary>
    /// The folder of the test.
    /// </summary>
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "golether-imports-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Creates the files under the folder of the test.
    /// </summary>
    private ImportedTrackFiles Files => new(_folder);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>
    /// The content and the name of the file reach the folder: the name is what the track list shows.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Import_CopiesTheFileUnderItsName()
    {
        var path = await Files.ImportAsync(new MemoryStream([1, 2, 3]), "film.ru.srt", LIMIT, TestContext.Current.CancellationToken);

        Assert.Equal("film.ru.srt", Path.GetFileName(path));
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A name comes from another application, so it can not lead out of the folder.
    /// </summary>
    /// <param name="name">The name the file claims.</param>
    [Theory]
    [InlineData("../../evil.srt")]
    [InlineData("..\\..\\evil.srt")]
    [InlineData("/etc/evil.srt")]
    public async Task Import_KeepsTheFileInsideTheFolder(string name)
    {
        var path = await Files.ImportAsync(new MemoryStream([1]), name, LIMIT, TestContext.Current.CancellationToken);

        Assert.StartsWith(Path.GetFullPath(_folder) + Path.DirectorySeparatorChar, Path.GetFullPath(path), StringComparison.Ordinal);
        Assert.Equal("evil.srt", Path.GetFileName(path));
    }

    /// <summary>
    /// A name with nothing usable still gives a file.
    /// </summary>
    /// <param name="name">The name the file claims.</param>
    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("/")]
    public async Task Import_NameWithoutFile_GivesAFile(string name)
    {
        var path = await Files.ImportAsync(new MemoryStream([1]), name, LIMIT, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(path));
        Assert.NotEqual(_folder, Path.GetDirectoryName(path));
    }

    /// <summary>
    /// Characters a file system or a track list does not like are replaced, and a long name is cut; the extension,
    /// by which the player knows the format, stays.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Import_CleansALongStrangeName()
    {
        var name = new string('я', 400) + "\n?*.srt";

        var path = await Files.ImportAsync(new MemoryStream([1]), name, LIMIT, TestContext.Current.CancellationToken);

        var result = Path.GetFileName(path);
        Assert.EndsWith(".srt", result, StringComparison.Ordinal);
        Assert.True(result.Length <= 120, result);
        Assert.DoesNotContain('\n', result);
        Assert.DoesNotContain('?', result);
    }

    /// <summary>
    /// Two files of one name do not overwrite each other: the first may still be playing.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Import_SameNameTwice_KeepsBoth()
    {
        var files = Files;

        var first = await files.ImportAsync(new MemoryStream([1]), "film.srt", LIMIT, TestContext.Current.CancellationToken);
        var second = await files.ImportAsync(new MemoryStream([2]), "film.srt", LIMIT, TestContext.Current.CancellationToken);

        Assert.NotEqual(first, second);
        Assert.Equal([1], await File.ReadAllBytesAsync(first, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A file over the limit is refused by its real size, and nothing of it stays on the device.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Import_OverTheLimit_LeavesNothing()
    {
        var files = Files;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => files.ImportAsync(new MemoryStream(new byte[LIMIT + 1]), "big.srt", LIMIT, TestContext.Current.CancellationToken));

        Assert.Equal(Texts.Format("Mobile.Tracks.TooLarge", 1), failure.Message);
        Assert.Empty(Directory.GetFiles(_folder, "*", SearchOption.AllDirectories));
    }

    /// <summary>
    /// Clearing takes everything imported from the disk.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Clear_RemovesTheFiles()
    {
        var files = Files;
        var path = await files.ImportAsync(new MemoryStream([1]), "film.srt", LIMIT, TestContext.Current.CancellationToken);

        files.Clear();

        Assert.False(File.Exists(path));
    }

    /// <summary>
    /// Clearing what was never created does nothing.
    /// </summary>
    [Fact]
    public void Clear_WithoutFiles_DoesNothing() => Files.Clear();
}
