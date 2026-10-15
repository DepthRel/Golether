using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Golether.Conventions.Tests;

/// <summary>
/// The C# sources of the repository the tests were built from, parsed once and shared by every convention test.
/// </summary>
internal static class RepositorySources
{
    /// <summary>
    /// The newest C# syntax, so every construct of the sources parses.
    /// </summary>
    private static readonly CSharpParseOptions _parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    /// <summary>
    /// The parsed files of each top folder of the repository.
    /// </summary>
    private static readonly ConcurrentDictionary<string, IReadOnlyList<SourceFile>> _parsed = new();

    /// <summary>
    /// The root of the repository.
    /// </summary>
    public static string Root { get; } = FindRoot();

    /// <summary>
    /// The sources of the applications and libraries.
    /// </summary>
    public static IReadOnlyList<SourceFile> Products => Folder("Sources");

    /// <summary>
    /// The sources of the applications, the libraries and the tests.
    /// </summary>
    public static IEnumerable<SourceFile> All => Folder("Sources").Concat(Folder("Tests"));

    /// <summary>
    /// Gives the path of a file relative to the root of the repository.
    /// </summary>
    /// <param name="path">The full path.</param>
    /// <returns>The relative path.</returns>
    public static string Relative(string path)
    {
        return Path.GetRelativePath(Root, path);
    }

    /// <summary>
    /// Lists the files under a folder of the repository, leaving out the build output.
    /// </summary>
    /// <param name="folder">The folder relative to the root.</param>
    /// <param name="pattern">The file name pattern.</param>
    /// <returns>The full paths of the files.</returns>
    public static IEnumerable<string> Files(string folder, string pattern)
    {
        var separator = Path.DirectorySeparatorChar;

        return Directory.EnumerateFiles(Path.Combine(Root, folder), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{separator}bin{separator}", StringComparison.Ordinal) &&
                !f.Contains($"{separator}obj{separator}", StringComparison.Ordinal));
    }

    /// <summary>
    /// Parses the C# files of a top folder of the repository, once.
    /// </summary>
    /// <param name="folder">The folder relative to the root.</param>
    /// <returns>The parsed files.</returns>
    private static IReadOnlyList<SourceFile> Folder(string folder)
    {
        return _parsed.GetOrAdd(folder, f => Files(f, "*.cs")
            .Select(Parse)
            .ToList());
    }

    /// <summary>
    /// Parses one C# file.
    /// </summary>
    /// <param name="path">The full path of the file.</param>
    /// <returns>The parsed file.</returns>
    private static SourceFile Parse(string path)
    {
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), _parseOptions, path);

        return new SourceFile(path, tree);
    }

    /// <summary>
    /// Finds the root of the repository: the nearest folder above the test binaries that holds Golether.slnx.
    /// </summary>
    /// <returns>The full path of the root.</returns>
    private static string FindRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Golether.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException(
            $"Golether.slnx was not found above '{AppContext.BaseDirectory}'; the convention tests need the sources.");
    }
}
