using Android.Content;
using Android.Provider;
using Golether.Media.Streaming.Files;
using Golether.Media.Streaming.Gallery;

namespace Golether.Mobile.Android;

/// <summary>
/// A folder the user chose in the picker of the system, with its nested folders: its files are listed and read through
/// the system. The pictures and videos get paths of their own (see <see cref="OpenedFiles"/>), opened when shown.
/// </summary>
internal sealed class DocumentTreeFolder : IGalleryDirectory
{
    /// <summary>
    /// The columns read of what a folder holds.
    /// </summary>
    private static readonly string[] _columns =
    [
        DocumentsContract.Document.ColumnDocumentId,
        DocumentsContract.Document.ColumnDisplayName,
        DocumentsContract.Document.ColumnMimeType,
        DocumentsContract.Document.ColumnSize,
    ];

    /// <summary>
    /// The resolver of the addresses.
    /// </summary>
    private readonly ContentResolver _resolver;

    /// <summary>
    /// The address of the chosen folder, which every address of the folder is made from.
    /// </summary>
    private readonly global::Android.Net.Uri _tree;

    /// <summary>
    /// The document of this folder in the chosen tree.
    /// </summary>
    private readonly string _documentId;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentTreeFolder"/> class.
    /// </summary>
    /// <param name="resolver">The resolver of the addresses.</param>
    /// <param name="tree">The address of the chosen folder.</param>
    /// <param name="documentId">The document of this folder.</param>
    /// <param name="name">The name of this folder.</param>
    private DocumentTreeFolder(ContentResolver resolver, global::Android.Net.Uri tree, string documentId, string name)
    {
        _resolver = resolver;
        _tree = tree;
        _documentId = documentId;
        Name = name;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>
    /// Opens the folder the picker gave.
    /// </summary>
    /// <param name="resolver">The resolver of the addresses.</param>
    /// <param name="tree">The address of the chosen folder.</param>
    /// <returns>The folder.</returns>
    /// <exception cref="IOException">The folder cannot be read.</exception>
    public static DocumentTreeFolder Open(ContentResolver resolver, global::Android.Net.Uri tree)
    {
        try
        {
            var root = DocumentsContract.GetTreeDocumentId(tree) ?? throw new IOException(tree.ToString());
            var name = ContentFiles.ReadName(resolver, DocumentsContract.BuildDocumentUriUsingTree(tree, root)!) ?? root;
            return new DocumentTreeFolder(resolver, tree, root, name);
        }
        catch (Java.Lang.Exception ex)
        {
            throw new IOException(ex.Message, ex);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<GalleryEntry> List()
    {
        var entries = new List<GalleryEntry>();
        try
        {
            using var cursor = _resolver.Query(DocumentsContract.BuildChildDocumentsUriUsingTree(_tree, _documentId)!, _columns, null, null, null)
                ?? throw new IOException(Name);
            while (cursor.MoveToNext())
            {
                if (Read(cursor) is { } entry)
                {
                    entries.Add(entry);
                }
            }
        }
        catch (Java.Lang.Exception ex)
        {
            throw new IOException(ex.Message, ex);
        }

        return entries;
    }

    /// <summary>
    /// Reads one thing the folder holds; hidden files are left out, as on a computer.
    /// </summary>
    /// <param name="cursor">The row.</param>
    /// <returns>The entry, or <see langword="null"/> for a hidden one.</returns>
    private GalleryEntry? Read(global::Android.Database.ICursor cursor)
    {
        var id = cursor.GetString(0);
        var name = cursor.GetString(1);
        if (id is null || string.IsNullOrEmpty(name) || name.StartsWith('.'))
        {
            return null;
        }

        if (cursor.GetString(2) == DocumentsContract.Document.MimeTypeDir)
        {
            return new GalleryEntry(name, string.Empty, 0, new DocumentTreeFolder(_resolver, _tree, id, name));
        }

        var length = cursor.IsNull(3) ? 0 : cursor.GetLong(3);
        if (!GalleryFolder.IsPicture(name) && !GalleryFolder.IsVideo(name))
        {
            // Never shown, so never opened: the walk only counts the RAW files among them.
            return new GalleryEntry(name, string.Empty, length, null);
        }

        var document = DocumentsContract.BuildDocumentUriUsingTree(_tree, id)!;
        var resolver = _resolver;
        return new GalleryEntry(name, OpenedFiles.Add(name, () => ContentFiles.Open(resolver, document)), length, null);
    }
}
