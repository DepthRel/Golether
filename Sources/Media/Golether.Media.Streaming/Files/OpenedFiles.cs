using System.Collections.Concurrent;
using System.Globalization;
using Golether.Core.Media;

namespace Golether.Media.Streaming.Files;

/// <summary>
/// The files the system gave the application as something to open rather than as a path: a document chosen on
/// Android is reached through the system, and the file under it may not be opened by name. Each gets a path of its
/// own that names it everywhere a path is expected (the shared film, the pictures of a folder, the player), while the
/// bytes are read through the opener the system gave.
/// </summary>
/// <remarks>
/// The paths lie in a folder that does not exist, so they never meet a real file; nothing registers here on the
/// desktop, and its files are opened by path as before.
/// </remarks>
public static class OpenedFiles
{
    /// <summary>
    /// The name of the folder the paths lie in, under the folder of temporary files.
    /// </summary>
    private const string ROOT_NAME = ".golether-opened";

    /// <summary>
    /// The name given to a file whose name is empty or cannot be used.
    /// </summary>
    private const string FALLBACK_NAME = "file";

    /// <summary>
    /// The folder the paths lie in.
    /// </summary>
    private static readonly string _root = Path.Combine(Path.GetTempPath(), ROOT_NAME);

    /// <summary>
    /// The openers by full path.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Func<Stream>> _openers = new(StringComparer.Ordinal);

    /// <summary>
    /// The number of the last file registered.
    /// </summary>
    private static long _last;

    /// <summary>
    /// Registers a file and gives it a path that ends with its name.
    /// </summary>
    /// <param name="name">The name of the file, as the user knows it.</param>
    /// <param name="open">Opens the file for reading; every call gives a new readable, seekable stream.</param>
    /// <returns>The full path of the file.</returns>
    public static string Add(string name, Func<Stream> open)
    {
        ArgumentNullException.ThrowIfNull(open);

        var id = Interlocked.Increment(ref _last).ToString(CultureInfo.InvariantCulture);
        var path = Path.Combine(_root, id, SafeName(name));
        _openers[path] = open;

        return path;
    }

    /// <summary>
    /// Checks whether a path is that of a registered file.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns><see langword="true"/> for a registered file.</returns>
    public static bool Contains(string path) => Find(path) is not null;

    /// <summary>
    /// Opens a registered file.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="stream">The new stream, or <see langword="null"/> when the path is not that of a registered file.</param>
    /// <returns><see langword="true"/> when the path is that of a registered file.</returns>
    /// <exception cref="IOException">The system could not open the file.</exception>
    public static bool TryOpen(string path, out Stream? stream)
    {
        var open = Find(path);
        stream = open?.Invoke();

        return stream is not null;
    }

    /// <summary>
    /// Forgets every registered file: what was chosen for a session is not opened after it.
    /// </summary>
    public static void Clear() => _openers.Clear();

    /// <summary>
    /// Finds the opener of a path.
    /// </summary>
    /// <param name="path">The path, or <see langword="null"/>.</param>
    /// <returns>The opener, or <see langword="null"/>.</returns>
    private static Func<Stream>? Find(string? path)
    {
        if (string.IsNullOrEmpty(path) || _openers.IsEmpty)
        {
            return null;
        }

        return _openers.TryGetValue(Path.GetFullPath(path), out var open) ? open : null;
    }

    /// <summary>
    /// Makes a name usable as the last part of a path on every system.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The name without separators and characters a path may not hold.</returns>
    private static string SafeName(string? name)
    {
        var normalized = MediaDescriptor.NormalizeFileName(name ?? string.Empty);
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string([.. normalized.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c)]).Trim();

        return safe.Length == 0 || safe is "." or ".." ? FALLBACK_NAME : safe;
    }
}
