using Avalonia.Platform.Storage;
using NSubstitute;
using Golether.Localization;
using Golether.Mobile.Services;

namespace Golether.Mobile.Tests;

/// <summary>
/// Tests of <see cref="MobileDialogs"/>: a file of the system picker comes as a stream and must end up as a path.
/// </summary>
public sealed class MobileDialogsTests : IDisposable
{
    /// <summary>
    /// The folder of the copies.
    /// </summary>
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "golether-dialogs-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// The picker of the system.
    /// </summary>
    private readonly IStorageProvider _storage = Substitute.For<IStorageProvider>();

    /// <summary>
    /// The texts shown to the user.
    /// </summary>
    private readonly List<string> _shown = [];

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>
    /// A chosen file is copied and its path given to the player.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task PickSubtitles_CopiesTheFile()
    {
        Choose(File("own.srt", new MemoryStream([1, 2, 3])));

        var path = await Create().PickSubtitleFileAsync();

        Assert.NotNull(path);
        Assert.Equal("own.srt", Path.GetFileName(path));
        Assert.Equal([1, 2, 3], await System.IO.File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A picker the user closed brings nothing and no message.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Pick_WhenNothingWasChosen_GivesNothing()
    {
        Choose();

        var path = await Create().PickAudioFileAsync();

        Assert.Null(path);
        Assert.Empty(_shown);
    }

    /// <summary>
    /// Without a window there is no picker.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Pick_WithoutAPicker_GivesNothing()
    {
        var dialogs = new MobileDialogs(() => null, new ImportedTrackFiles(_folder), _shown.Add);

        Assert.Null(await dialogs.PickAudioFileAsync());
    }

    /// <summary>
    /// A file that cannot be read is explained, it does not fail the screen.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Pick_WhenTheFileCannotBeRead_Explains()
    {
        var file = Substitute.For<IStorageFile>();
        file.Name.Returns("own.m4a");
        file.OpenReadAsync().Returns<Task<Stream>>(_ => throw new IOException("нет доступа"));
        Choose(file);

        var path = await Create().PickAudioFileAsync();

        Assert.Null(path);
        Assert.Contains("нет доступа", Assert.Single(_shown));
        Assert.StartsWith(Texts.Get("Tracks.Error.AudioTitle"), _shown[0]);
    }

    /// <summary>
    /// An error names what failed and why.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task ShowError_JoinsTheTitleAndTheReason()
    {
        await Create().ShowErrorAsync("Субтитры", "файл пуст");

        Assert.Equal("Субтитры: файл пуст", Assert.Single(_shown));
    }

    /// <summary>
    /// Describes a file of the picker.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="content">The content.</param>
    /// <returns>The file.</returns>
    private static IStorageFile File(string name, Stream content)
    {
        var file = Substitute.For<IStorageFile>();
        file.Name.Returns(name);
        file.OpenReadAsync().Returns(content);

        return file;
    }

    /// <summary>
    /// Creates the dialogs over the picker.
    /// </summary>
    /// <returns>The dialogs.</returns>
    private MobileDialogs Create() => new(() => _storage, new ImportedTrackFiles(_folder), _shown.Add);

    /// <summary>
    /// Sets what the user chooses in the picker.
    /// </summary>
    /// <param name="files">The files.</param>
    private void Choose(params IStorageFile[] files)
        => _storage.OpenFilePickerAsync(Arg.Any<FilePickerOpenOptions>()).Returns(files);
}
