using System.Text;
using Golether.Localization;

namespace Golether.Mobile.Services;

/// <summary>
/// The files of tracks the user brought from other applications. The system hands over a stream, not a path, and the
/// player needs a path, so the file is copied to the cache and removed with the session.
/// </summary>
public sealed class ImportedTrackFiles
{
    /// <summary>
    /// The longest file name kept.
    /// </summary>
    private const int MAX_NAME_LENGTH = 120;

    /// <summary>
    /// The name of a file that came without a usable one.
    /// </summary>
    private const string DEFAULT_NAME = "track";

    /// <summary>
    /// The folder of the copies.
    /// </summary>
    private readonly string _folder;

    /// <summary>
    /// The number of the last copy: every copy has a folder of its own, so a name stays what the user knows it by.
    /// </summary>
    private int _last;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportedTrackFiles"/> class.
    /// </summary>
    /// <param name="folder">The folder of the copies.</param>
    public ImportedTrackFiles(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        _folder = folder;
    }

    /// <summary>
    /// Copies a file to the cache.
    /// </summary>
    /// <param name="source">The content of the file.</param>
    /// <param name="name">The name the file claims.</param>
    /// <param name="maxSize">The largest allowed size in bytes; the real size counts, not a reported one.</param>
    /// <param name="cancellationToken">Stops the copying.</param>
    /// <returns>The path of the copy.</returns>
    /// <exception cref="InvalidOperationException">The file is too large; the message is for the user.</exception>
    public async Task<string> ImportAsync(Stream source, string name, long maxSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var folder = Path.Combine(_folder, Interlocked.Increment(ref _last).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, CleanName(name));

        try
        {
            await Task.Run(() => CopyAsync(source, path, maxSize, cancellationToken), cancellationToken);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            Remove(folder);
            throw;
        }

        return path;
    }

    /// <summary>
    /// Removes the copies; a copy the system still holds stays until the next time.
    /// </summary>
    public void Clear() => Remove(_folder);

    /// <summary>
    /// Copies the content while counting it.
    /// </summary>
    /// <param name="source">The content.</param>
    /// <param name="path">The target.</param>
    /// <param name="maxSize">The largest allowed size in bytes.</param>
    /// <param name="cancellationToken">Stops the copying.</param>
    /// <returns>A task that completes when the copy is written.</returns>
    private static async Task CopyAsync(Stream source, string path, long maxSize, CancellationToken cancellationToken)
    {
        const int BUFFER_SIZE = 81920;
        const int BYTES_IN_MEGABYTE = 1024 * 1024;

        await using var target = File.Create(path);
        var buffer = new byte[BUFFER_SIZE];
        var total = 0L;

        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxSize)
            {
                var limit = Math.Max(maxSize / BYTES_IN_MEGABYTE, 1);
                throw new InvalidOperationException(Texts.Format("Mobile.Tracks.TooLarge", limit));
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    /// <summary>
    /// Makes a safe file name of what another application claims: no folders, no special characters, no endless length.
    /// </summary>
    /// <param name="name">The claimed name.</param>
    /// <returns>The name of the copy.</returns>
    private static string CleanName(string name)
    {
        var last = name.Replace('\\', '/').Split('/')[^1];

        var clean = new StringBuilder(last.Length);
        foreach (var symbol in last)
        {
            clean.Append(char.IsLetterOrDigit(symbol) || symbol is '.' or '-' or '_' or ' ' ? symbol : '_');
        }

        var result = clean.ToString().Trim();
        if (result.Trim('.', '_').Length == 0)
        {
            return DEFAULT_NAME;
        }

        var extension = Path.GetExtension(result);
        return result.Length <= MAX_NAME_LENGTH || extension.Length >= MAX_NAME_LENGTH
            ? result[..Math.Min(result.Length, MAX_NAME_LENGTH)]
            : result[..(MAX_NAME_LENGTH - extension.Length)] + extension;
    }

    /// <summary>
    /// Removes a folder; a failure leaves the files for the next cleaning and for the system, which owns the cache.
    /// </summary>
    /// <param name="folder">The folder.</param>
    private static void Remove(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }
}
