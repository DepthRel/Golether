using System.Text.Json;

namespace Golether.UI.Services;

/// <summary>
/// <see cref="IAvatarStore"/> that keeps three files in the avatars directory: the round avatar shown in calls
/// (<c>avatar.png</c>), the original image it was made from (<c>original</c>) and the framing (<c>framing.json</c>).
/// </summary>
public sealed class AvatarStore : IAvatarStore
{
    /// <summary>
    /// The round avatar shown in calls.
    /// </summary>
    private const string AvatarFileName = "avatar.png";

    /// <summary>
    /// The original image the avatar was cropped from.
    /// </summary>
    private const string OriginalFileName = "original";

    /// <summary>
    /// The framing of the original inside the round avatar.
    /// </summary>
    private const string FramingFileName = "framing.json";

    /// <summary>
    /// The avatars directory.
    /// </summary>
    private readonly string _directory;

    /// <summary>
    /// Initializes a new instance of the <see cref="AvatarStore"/> class.
    /// </summary>
    /// <param name="directory">The avatars directory.</param>
    public AvatarStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    /// <inheritdoc />
    public async Task<byte[]?> LoadCurrentAsync() => await ReadAsync(AvatarFileName).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<StoredAvatar?> LoadForEditingAsync()
    {
        var original = await ReadAsync(OriginalFileName).ConfigureAwait(false);
        if (original is null)
        {
            return null;
        }

        var framing = await ReadFramingAsync().ConfigureAwait(false);
        return new StoredAvatar(original, framing ?? new AvatarFraming(1, 0, 0));
    }

    /// <inheritdoc />
    public async Task SaveAsync(AvatarSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(Path.Combine(_directory, AvatarFileName), selection.Avatar).ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(_directory, OriginalFileName), selection.Original).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(_directory, FramingFileName), JsonSerializer.Serialize(selection.Framing)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task ClearAsync()
    {
        foreach (var name in new[] { AvatarFileName, OriginalFileName, FramingFileName })
        {
            try
            {
                var path = Path.Combine(_directory, name);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A locked or read-only file is left; the avatar will simply reappear next time.
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads a file of the avatars directory.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <returns>The bytes, or <see langword="null"/> when the file is missing or unreadable.</returns>
    private async Task<byte[]?> ReadAsync(string name)
    {
        try
        {
            var path = Path.Combine(_directory, name);
            return File.Exists(path) ? await File.ReadAllBytesAsync(path).ConfigureAwait(false) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the stored framing.
    /// </summary>
    /// <returns>The framing, or <see langword="null"/> when it is missing or unreadable.</returns>
    private async Task<AvatarFraming?> ReadFramingAsync()
    {
        var bytes = await ReadAsync(FramingFileName).ConfigureAwait(false);
        if (bytes is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AvatarFraming>(bytes);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
