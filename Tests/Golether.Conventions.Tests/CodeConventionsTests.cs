using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Golether.Conventions.Tests;

/// <summary>
/// Tests of the code style of AGENTS.md that the compiler and the analyzers do not check: files and types, XML
/// comments, enumerations, logical operators and the order of using directives.
/// </summary>
public sealed class CodeConventionsTests
{
    /// <summary>
    /// A file declares at most one top-level type, and the file is named after it; a partial part may add a suffix
    /// (<c>MainWindow.axaml.cs</c>, <c>WebRtcPeer.Quality.cs</c>).
    /// </summary>
    [Fact]
    public void Files_HoldOneTypeNamedAfterThem()
    {
        var findings = new List<string>();

        foreach (var file in RepositorySources.All)
        {
            var types = file.Root.DescendantNodes(n => n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax)
                .OfType<MemberDeclarationSyntax>()
                .Where(IsType)
                .ToList();
            var name = Path.GetFileName(file.Path).Split('.')[0];

            if (types.Count > 1)
            {
                findings.Add($"{file.Relative}: {types.Count} types");
            }
            else if (types.Count == 1 && NameOf(types[0]) != name)
            {
                findings.Add($"{file.Relative}: declares {NameOf(types[0])}");
            }
        }

        ConventionAssert.None(findings, "A file with more than one type, or not named after its type:");
    }

    /// <summary>
    /// A type declared inside another type is private: only private nested types share a file with their owner.
    /// </summary>
    [Fact]
    public void NestedTypes_ArePrivate()
    {
        var findings = RepositorySources.All
            .SelectMany(f => f.Root.DescendantNodes()
                .OfType<MemberDeclarationSyntax>()
                .Where(t => IsType(t) &&
                    t.Parent is TypeDeclarationSyntax &&
                    HasAccess(t) &&
                    !t.Modifiers.Any(SyntaxKind.PrivateKeyword))
                .Select(t => f.At(t)));

        ConventionAssert.None(findings, "A nested type that is not private:");
    }

    /// <summary>
    /// An enumeration lives in the Enums folder at the root of its project: the shared ones in Golether.Core or
    /// Golether.Core.Data, the ones of a single application or library in that project.
    /// </summary>
    [Fact]
    public void Enums_LiveInTheEnumsFolderOfTheirProject()
    {
        var findings = RepositorySources.Products
            .Where(f => DeclaresTopLevelEnum(f) &&
                !IsInEnumsOfItsProject(f.Path))
            .Select(f => f.Relative);

        ConventionAssert.None(findings, "An enum outside the Enums folder at the root of its project:");
    }

    /// <summary>
    /// Every type, method, constructor, property, field, event, operator and enumeration value carries an XML comment,
    /// private members and tests included.
    /// </summary>
    [Fact]
    public void Declarations_HaveXmlComments()
    {
        var findings = RepositorySources.All
            .SelectMany(f => f.Root.DescendantNodes()
                .OfType<MemberDeclarationSyntax>()
                .Where(m => NeedsComment(m) &&
                    !HasXmlComment(m))
                .Select(m => f.At(m)));

        ConventionAssert.None(findings, "A declaration without an XML comment:");
    }

    /// <summary>
    /// A logical operator stays at the end of a line; a continuation line never starts with <c>&amp;&amp;</c> or
    /// <c>||</c>.
    /// </summary>
    [Fact]
    public void LogicalOperators_EndTheLine()
    {
        var findings = RepositorySources.All
            .SelectMany(f => f.Root.DescendantNodes()
                .OfType<BinaryExpressionSyntax>()
                .Where(b => IsLogical(b) &&
                    f.StartLine(b.OperatorToken) > f.EndLine(b.Left))
                .Select(b => f.At(b.OperatorToken)));

        ConventionAssert.None(findings, "A line that starts with a logical operator:");
    }

    /// <summary>
    /// The using directives of a file go in three blocks one after another, System, then libraries, then Golether, each
    /// in alphabetical order; static and alias directives follow the blocks.
    /// </summary>
    [Fact]
    public void Usings_GoSystemThenLibrariesThenGolether()
    {
        var findings = RepositorySources.All
            .Where(f => !UsingsAreOrdered(f.Root.Usings))
            .Select(f => f.Relative);

        ConventionAssert.None(findings, "Using directives out of the order System, libraries, Golether:");
    }

    /// <summary>
    /// Tells whether a declaration is a type.
    /// </summary>
    /// <param name="member">The declaration.</param>
    /// <returns><see langword="true"/> for a class, struct, record, interface, enum or delegate.</returns>
    private static bool IsType(MemberDeclarationSyntax member)
    {
        return member is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax;
    }

    /// <summary>
    /// Gives the name of a type declaration.
    /// </summary>
    /// <param name="type">The declaration of a type.</param>
    /// <returns>The name without type parameters.</returns>
    private static string NameOf(MemberDeclarationSyntax type)
    {
        return type switch
        {
            BaseTypeDeclarationSyntax declared => declared.Identifier.Text,
            DelegateDeclarationSyntax delegated => delegated.Identifier.Text,
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Tells whether a file declares an enumeration outside of any type.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <returns><see langword="true"/> when the file declares a top-level enum.</returns>
    private static bool DeclaresTopLevelEnum(SourceFile file)
    {
        return file.Root.DescendantNodes()
            .OfType<EnumDeclarationSyntax>()
            .Any(e => e.Parent is not TypeDeclarationSyntax);
    }

    /// <summary>
    /// Tells whether a binary expression is a logical and or a logical or.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <returns><see langword="true"/> for <c>&amp;&amp;</c> and <c>||</c>.</returns>
    private static bool IsLogical(BinaryExpressionSyntax expression)
    {
        return expression.IsKind(SyntaxKind.LogicalAndExpression) ||
            expression.IsKind(SyntaxKind.LogicalOrExpression);
    }

    /// <summary>
    /// Tells whether a declaration states an access modifier.
    /// </summary>
    /// <param name="member">The declaration.</param>
    /// <returns><see langword="true"/> when an access modifier is written.</returns>
    private static bool HasAccess(MemberDeclarationSyntax member)
    {
        return member.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword) ||
            m.IsKind(SyntaxKind.InternalKeyword) ||
            m.IsKind(SyntaxKind.ProtectedKeyword) ||
            m.IsKind(SyntaxKind.PrivateKeyword));
    }

    /// <summary>
    /// Tells whether the rule of XML comments covers a declaration.
    /// </summary>
    /// <param name="member">The declaration.</param>
    /// <returns><see langword="true"/> for types, members and enumeration values.</returns>
    private static bool NeedsComment(MemberDeclarationSyntax member)
    {
        return member is BaseTypeDeclarationSyntax or
            DelegateDeclarationSyntax or
            BaseMethodDeclarationSyntax or
            BasePropertyDeclarationSyntax or
            BaseFieldDeclarationSyntax or
            EnumMemberDeclarationSyntax;
    }

    /// <summary>
    /// Tells whether a declaration starts with an XML documentation comment.
    /// </summary>
    /// <param name="member">The declaration.</param>
    /// <returns><see langword="true"/> when a <c>///</c> comment precedes it.</returns>
    private static bool HasXmlComment(MemberDeclarationSyntax member)
    {
        return member.GetLeadingTrivia().Any(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
            t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));
    }

    /// <summary>
    /// Tells whether a source file lies in the Enums folder at the root of its project, the project being the nearest
    /// folder above the file with a project file.
    /// </summary>
    /// <param name="file">The full path of the source file.</param>
    /// <returns><see langword="true"/> when the file is in that folder.</returns>
    private static bool IsInEnumsOfItsProject(string file)
    {
        var folder = new DirectoryInfo(Path.GetDirectoryName(file)!);
        while (folder is not null && folder.GetFiles("*.csproj").Length == 0)
        {
            folder = folder.Parent;
        }

        if (folder is null)
        {
            throw new InvalidOperationException($"No project file above '{file}'.");
        }

        var enums = Path.Combine(folder.FullName, "Enums");

        return string.Equals(Path.GetDirectoryName(file), enums, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tells whether the using directives of a file follow the order of the code rules.
    /// </summary>
    /// <param name="usings">The using directives at the top of the file.</param>
    /// <returns><see langword="true"/> when the order is right.</returns>
    private static bool UsingsAreOrdered(SyntaxList<UsingDirectiveSyntax> usings)
    {
        var plain = usings
            .TakeWhile(IsPlain)
            .Select(u => u.NamespaceOrType.ToString())
            .ToList();

        if (usings.Skip(plain.Count).Any(IsPlain))
        {
            return false;
        }

        var expected = plain
            .OrderBy(BlockOf)
            .ThenBy(ns => ns, Comparer<string>.Create(CompareNamespaces));

        return plain.SequenceEqual(expected);
    }

    /// <summary>
    /// Tells whether a using directive is a plain one: neither global, static nor an alias.
    /// </summary>
    /// <param name="directive">The directive.</param>
    /// <returns><see langword="true"/> for a plain directive.</returns>
    private static bool IsPlain(UsingDirectiveSyntax directive)
    {
        return directive.GlobalKeyword.IsKind(SyntaxKind.None) &&
            directive.StaticKeyword.IsKind(SyntaxKind.None) &&
            directive.Alias is null;
    }

    /// <summary>
    /// Gives the block of a namespace: System first, then the libraries, then the solution.
    /// </summary>
    /// <param name="ns">The namespace.</param>
    /// <returns>0 for System, 1 for a library, 2 for Golether.</returns>
    private static int BlockOf(string ns)
    {
        if (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal))
        {
            return 0;
        }

        if (ns == "Golether" || ns.StartsWith("Golether.", StringComparison.Ordinal))
        {
            return 2;
        }

        return 1;
    }

    /// <summary>
    /// Compares two namespaces segment by segment, ignoring case first, as the IDE sorts them.
    /// </summary>
    /// <param name="left">The first namespace.</param>
    /// <param name="right">The second namespace.</param>
    /// <returns>A negative number, zero or a positive number, as for any comparison.</returns>
    private static int CompareNamespaces(string left, string right)
    {
        var leftSegments = left.Split('.');
        var rightSegments = right.Split('.');

        for (var i = 0; i < Math.Min(leftSegments.Length, rightSegments.Length); i++)
        {
            var order = StringComparer.OrdinalIgnoreCase.Compare(leftSegments[i], rightSegments[i]);
            if (order == 0)
            {
                order = StringComparer.Ordinal.Compare(leftSegments[i], rightSegments[i]);
            }

            if (order != 0)
            {
                return order;
            }
        }

        return leftSegments.Length.CompareTo(rightSegments.Length);
    }
}
