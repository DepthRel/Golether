using Golether.Core.Enums;

namespace Golether.Core.Media;

/// <summary>
/// An item of the folder the host shows: what a participant needs to list it and, for a picture, to fetch it.
/// </summary>
/// <param name="FileName">The file name shown to participants (no directory part).</param>
/// <param name="Length">The number of bytes: of the picture a participant receives (without its metadata), or of the
/// video.</param>
/// <param name="Key">The identifier a picture is fetched by: 64 hexadecimal digits, random for each showing.</param>
public sealed record GalleryItem(string FileName, long Length, string Key)
{
    /// <summary>
    /// The most items one folder may show: the list goes to every participant, and a folder of tens of thousands
    /// of files is an archive, not something looked at together.
    /// </summary>
    public const int MAX_ITEMS = 2000;

    /// <summary>
    /// The largest picture shown, in bytes: a photograph of a modern camera fits many times over.
    /// </summary>
    public const long MAX_LENGTH = 64L * 1024 * 1024;

    /// <summary>
    /// The longest path of a nested folder shown with its items.
    /// </summary>
    public const int MAX_FOLDER_LENGTH = 128;

    /// <summary>
    /// The separator of the nested folders in <see cref="Folder"/>.
    /// </summary>
    public const char FOLDER_SEPARATOR = '/';

    /// <summary>
    /// Gets what the item is.
    /// </summary>
    public GalleryItemKind Kind { get; init; }

    /// <summary>
    /// Gets the nested folder the item lies in, relative to the shown folder: names joined by
    /// <see cref="FOLDER_SEPARATOR"/>, empty for the shown folder itself. It is only shown, never used as a path.
    /// </summary>
    public string Folder { get; init; } = string.Empty;

    /// <summary>
    /// Makes a relative folder path harmless to show: every name is cleaned like a file name, empty names and
    /// references to a parent or to the folder itself are dropped, and the path is cut to
    /// <see cref="MAX_FOLDER_LENGTH"/>.
    /// </summary>
    /// <param name="folder">The relative path; either separator is accepted.</param>
    /// <returns>The cleaned path, empty for the shown folder itself.</returns>
    public static string NormalizeFolder(string? folder)
    {
        var names = (folder ?? string.Empty)
            .Split(['/', '\\'],StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(n => n is not ("." or ".."))
            .Select(MediaDescriptor.NormalizeFileName);
        var joined = string.Join(FOLDER_SEPARATOR, names);
        if (joined.Length > MAX_FOLDER_LENGTH)
        {
            joined = joined[..MAX_FOLDER_LENGTH].TrimEnd(FOLDER_SEPARATOR, ' ');
        }

        return joined;
    }

    /// <summary>
    /// Returns the descriptor a picture is fetched by over the media data streams.
    /// </summary>
    /// <returns>The descriptor.</returns>
    public MediaDescriptor ToDescriptor() => new()
    {
        FileName = FileName,
        Length = Length,
        QuickId = Key,
    };

    /// <summary>
    /// Checks an item received from the network.
    /// </summary>
    /// <returns><see langword="true"/> when the kind, the name, the folder, the length and the key are acceptable.</returns>
    public bool IsValid()
    {
        if (!Enum.IsDefined(Kind) || !string.Equals(Folder, NormalizeFolder(Folder), StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            ToDescriptor().Validate();
        }
        catch (ArgumentException)
        {
            return false;
        }

        return Length > 0 && (Kind == GalleryItemKind.Video || Length <= MAX_LENGTH);
    }
}
