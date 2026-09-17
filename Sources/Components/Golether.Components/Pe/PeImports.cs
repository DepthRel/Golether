using System.Reflection.PortableExecutable;
using System.Text;

namespace Golether.Components.Pe;

/// <summary>
/// Reads the names of the DLLs a Windows executable imports (regular and delay-loaded imports).
/// </summary>
/// <remarks>
/// Used to copy only the libraries GStreamer plugins really need instead of the whole installation.
/// </remarks>
public static class PeImports
{
    /// <summary>
    /// The size of <c>IMAGE_IMPORT_DESCRIPTOR</c>.
    /// </summary>
    private const int ImportDescriptorSize = 20;

    /// <summary>
    /// The size of <c>IMAGE_DELAYLOAD_DESCRIPTOR</c>.
    /// </summary>
    private const int DelayDescriptorSize = 32;

    /// <summary>
    /// The upper bound of descriptors read from one file (protects against malformed files).
    /// </summary>
    private const int MaxDescriptors = 4096;

    /// <summary>
    /// Reads the imported DLL names of a file.
    /// </summary>
    /// <param name="path">The executable or DLL.</param>
    /// <returns>The distinct DLL names as written in the file.</returns>
    /// <exception cref="BadImageFormatException">The file is not a valid PE image.</exception>
    public static IReadOnlyList<string> Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>
    /// Reads the imported DLL names of an image.
    /// </summary>
    /// <param name="image">The image stream.</param>
    /// <returns>The distinct DLL names as written in the image.</returns>
    /// <exception cref="BadImageFormatException">The stream is not a valid PE image.</exception>
    public static IReadOnlyList<string> Read(Stream image)
    {
        using var reader = new PEReader(image, PEStreamOptions.LeaveOpen | PEStreamOptions.PrefetchEntireImage);
        var header = reader.PEHeaders.PEHeader ?? throw new BadImageFormatException("The image has no optional header.");
        var names = new List<string>();
        ReadDescriptors(reader, header.ImportTableDirectory.RelativeVirtualAddress, ImportDescriptorSize, nameOffset: 12, names);
        ReadDescriptors(reader, header.DelayImportTableDirectory.RelativeVirtualAddress, DelayDescriptorSize, nameOffset: 4, names);
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// Reads a table of descriptors that end with an all-zero entry.
    /// </summary>
    /// <param name="reader">The PE reader.</param>
    /// <param name="tableRva">The table address, 0 when absent.</param>
    /// <param name="descriptorSize">The descriptor size.</param>
    /// <param name="nameOffset">The offset of the name RVA inside a descriptor.</param>
    /// <param name="names">Receives the names.</param>
    private static void ReadDescriptors(PEReader reader, int tableRva, int descriptorSize, int nameOffset, List<string> names)
    {
        if (tableRva == 0)
        {
            return;
        }

        var table = reader.GetSectionData(tableRva);
        for (var index = 0; index < MaxDescriptors; index++)
        {
            var offset = index * descriptorSize;
            if (offset + descriptorSize > table.Length)
            {
                return;
            }

            var descriptor = table.GetReader(offset, descriptorSize);
            var bytes = descriptor.ReadBytes(descriptorSize);
            if (bytes.All(b => b == 0))
            {
                return;
            }

            var nameRva = BitConverter.ToInt32(bytes, nameOffset);
            if (nameRva > 0 && ReadAsciiZ(reader, nameRva) is { Length: > 0 } name)
            {
                names.Add(name);
            }
        }
    }

    /// <summary>
    /// Reads a zero-terminated ASCII string at an address.
    /// </summary>
    /// <param name="reader">The PE reader.</param>
    /// <param name="rva">The address.</param>
    /// <returns>The string, or <see langword="null"/> when the address is outside the image.</returns>
    private static string? ReadAsciiZ(PEReader reader, int rva)
    {
        var block = reader.GetSectionData(rva);
        if (block.Length == 0)
        {
            return null;
        }

        var bytes = block.GetReader(0, Math.Min(block.Length, 512)).ReadBytes(Math.Min(block.Length, 512));
        var end = Array.IndexOf(bytes, (byte)0);
        return Encoding.ASCII.GetString(bytes, 0, end < 0 ? bytes.Length : end);
    }
}
