using System.IO;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EntityTracker.Wpf.Tests.Diagnostics;

/// <summary>
/// Every caught exception is logged. A catch block in src must log (a Log... call), pass the exception
/// on (throw), or say why it can't with a "// Not logged: reason" comment (only the log itself).
/// </summary>
public sealed class LoggingCoverageTests
{
    private const string Exemption = "// Not logged:";

    [Fact]
    public void EveryCatchBlockLogsRethrowsOrSaysWhy()
    {
        string root = FindRepositoryRoot();
        string[] files = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                                  !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToArray();
        Assert.NotEmpty(files);

        List<string> offenders = [];
        foreach (string file in files)
        {
            SyntaxNode tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file),
                new CSharpParseOptions(LanguageVersion.Preview)).GetRoot();
            foreach (CatchClauseSyntax clause in tree.DescendantNodes().OfType<CatchClauseSyntax>())
            {
                if (Logs(clause.Block) || Throws(clause.Block) || IsExempt(clause.Block)) continue;
                int line = clause.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                offenders.Add($"{Path.GetRelativePath(root, file)}:{line}: {clause.CatchKeyword}{clause.Declaration}{clause.Filter}");
            }
        }

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} catch blocks neither log, rethrow nor explain why (\"{Exemption} ...\"):\n" +
            string.Join('\n', offenders));
    }

    private static bool Logs(BlockSyntax block) =>
        block.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(static call => call.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.Text.StartsWith("Log", StringComparison.Ordinal),
            IdentifierNameSyntax name => name.Identifier.Text.StartsWith("Log", StringComparison.Ordinal),
            MemberBindingExpressionSyntax binding => binding.Name.Identifier.Text.StartsWith("Log", StringComparison.Ordinal),
            _ => false
        });

    private static bool Throws(BlockSyntax block) =>
        block.DescendantNodes().Any(static node => node is ThrowStatementSyntax or ThrowExpressionSyntax);

    private static bool IsExempt(BlockSyntax block) =>
        block.DescendantTrivia().Any(static trivia =>
            trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && trivia.ToString().StartsWith(Exemption, StringComparison.Ordinal));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "EntityTracker.slnx")))
            current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
