using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Golether.Conventions.Tests;

/// <summary>
/// A parsed C# file of the repository.
/// </summary>
/// <param name="Path">The full path of the file.</param>
/// <param name="Tree">The syntax tree of the file.</param>
internal sealed record SourceFile(string Path, SyntaxTree Tree)
{
    /// <summary>
    /// The root of the syntax tree.
    /// </summary>
    public CompilationUnitSyntax Root => (CompilationUnitSyntax)Tree.GetRoot();

    /// <summary>
    /// The path of the file relative to the root of the repository.
    /// </summary>
    public string Relative => RepositorySources.Relative(Path);

    /// <summary>
    /// Tells where a piece of syntax is, for readable failures.
    /// </summary>
    /// <param name="syntax">The node or token.</param>
    /// <returns>The relative path and the line number, as <c>path:line</c>.</returns>
    public string At(SyntaxNodeOrToken syntax)
    {
        var line = Tree.GetLineSpan(syntax.Span).StartLinePosition.Line + 1;

        return $"{Relative}:{line}";
    }

    /// <summary>
    /// Tells on which line a piece of syntax starts.
    /// </summary>
    /// <param name="syntax">The node or token.</param>
    /// <returns>The zero-based line number.</returns>
    public int StartLine(SyntaxNodeOrToken syntax)
    {
        return Tree.GetLineSpan(syntax.Span).StartLinePosition.Line;
    }

    /// <summary>
    /// Tells on which line a piece of syntax ends.
    /// </summary>
    /// <param name="syntax">The node or token.</param>
    /// <returns>The zero-based line number.</returns>
    public int EndLine(SyntaxNodeOrToken syntax)
    {
        return Tree.GetLineSpan(syntax.Span).EndLinePosition.Line;
    }
}
