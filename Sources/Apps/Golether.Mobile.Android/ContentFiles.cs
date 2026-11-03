using Android.Content;
using Android.Provider;
using Microsoft.Win32.SafeHandles;
using Golether.Localization;

namespace Golether.Mobile.Android;

/// <summary>
/// Reads the files the user chose in the pickers of the system: they are reached by their addresses, through the
/// system, and never by a path the application could open itself.
/// </summary>
internal static class ContentFiles
{
    /// <summary>
    /// The columns read of a chosen file: its name and its size.
    /// </summary>
    private static readonly string[] _nameAndSize = [IOpenableColumns.DisplayName, IOpenableColumns.Size];

    /// <summary>
    /// Opens a file for reading from any place in it: the system gives a descriptor of the file, read without a copy.
    /// </summary>
    /// <param name="resolver">The resolver of the addresses.</param>
    /// <param name="document">The address of the file.</param>
    /// <returns>A new stream.</returns>
    /// <exception cref="IOException">The file cannot be opened.</exception>
    public static Stream Open(ContentResolver resolver, global::Android.Net.Uri document)
    {
        try
        {
            using var descriptor = resolver.OpenFileDescriptor(document, "r")
                ?? throw new IOException(Texts.Get("Mobile.Document.NotSeekable"));

            // The stream owns the descriptor from now on and closes it with itself.
            var handle = new SafeFileHandle(descriptor.DetachFd(), ownsHandle: true);
            return new FileStream(handle, FileAccess.Read, bufferSize: 0);
        }
        catch (Java.Lang.Exception ex)
        {
            throw new IOException(ex.Message, ex);
        }
    }

    /// <summary>
    /// Reads the name of a chosen file as the user knows it.
    /// </summary>
    /// <param name="resolver">The resolver of the addresses.</param>
    /// <param name="document">The address of the file.</param>
    /// <returns>The name, or <see langword="null"/> when the system gives none.</returns>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static string? ReadName(ContentResolver resolver, global::Android.Net.Uri document)
    {
        try
        {
            using var cursor = resolver.Query(document, _nameAndSize, null, null, null);
            return cursor is not null && cursor.MoveToFirst() && !cursor.IsNull(0) ? cursor.GetString(0) : null;
        }
        catch (Java.Lang.Exception ex)
        {
            throw new IOException(ex.Message, ex);
        }
    }
}
