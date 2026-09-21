using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

return DocumentationAudit.Run(args);

/// <summary>Audits named declarations using the SDK's C# parser rather than a line-count approximation.</summary>
internal static class DocumentationAudit
{
    /// <summary>Checks C# syntax and attached summaries for runtime, test and development-tool source files.</summary>
    /// <param name="args">Optional repository root; the current working directory is used otherwise.</param>
    /// <returns>Zero only if every scanned declaration has a nonempty XML summary and valid parameter documentation.</returns>
    public static int Run(string[] args)
    {
        string root = Path.GetFullPath(args.Length == 0 ? Directory.GetCurrentDirectory() : args[0]);
        if (!File.Exists(Path.Combine(root, "LuckyStarPspToolkit.sln")))
        {
            Console.Error.WriteLine("Repository root was not found.");
            return 2;
        }
        int count = 0;
        var failures = new List<string>();
        foreach (string area in new[] { "src", "tests", "tools" })
        {
            foreach (string path in Directory.EnumerateFiles(Path.Combine(root, area), "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(root, path);
                if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
                {
                    continue;
                }
                SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path),
                    new CSharpParseOptions(LanguageVersion.CSharp13, DocumentationMode.Diagnose), path);
                foreach (Diagnostic diagnostic in tree.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error))
                {
                    failures.Add(diagnostic.ToString());
                }
                foreach (SyntaxNode node in tree.GetRoot().DescendantNodes().Where(RequiresSummary))
                {
                    count++;
                    string label = $"{relative}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}";
                    DocumentationCommentTriviaSyntax? comment = node.GetLeadingTrivia()
                        .Select(trivia => trivia.GetStructure()).OfType<DocumentationCommentTriviaSyntax>().LastOrDefault();
                    if (comment is null)
                    {
                        failures.Add($"{label}: Missing XML documentation for {node.Kind()}.");
                        continue;
                    }
                    try
                    {
                        string fragment = Regex.Replace(comment.ToFullString(), @"(?m)^\s*/// ?", string.Empty);
                        XElement xml = XElement.Parse("<member>" + fragment + "</member>");
                        XElement? summary = xml.Element("summary");
                        if (summary is null || string.IsNullOrWhiteSpace(summary.Value))
                        {
                            failures.Add($"{label}: Empty or missing summary.");
                        }
                        // Inspect direct parameter-list children: methods, primary constructors,
                        // delegates, local functions and indexers share these syntax nodes.
                        IEnumerable<ParameterSyntax> parameters = node.ChildNodes()
                            .Where(child => child is ParameterListSyntax or BracketedParameterListSyntax)
                            .SelectMany(child => child.ChildNodes().OfType<ParameterSyntax>());
                        foreach (ParameterSyntax parameter in parameters)
                        {
                            string name = parameter.Identifier.ValueText;
                            if (!xml.Elements("param").Any(item => (string?)item.Attribute("name") == name
                                && !string.IsNullOrWhiteSpace(item.Value)))
                            {
                                failures.Add($"{label}: Missing documentation for parameter '{name}'.");
                            }
                        }
                    }
                    catch (System.Xml.XmlException ex)
                    {
                        failures.Add($"{label}: Malformed XML documentation: {ex.Message}");
                    }
                }
            }
        }
        foreach (string failure in failures)
        {
            Console.Error.WriteLine(failure);
        }
        Console.WriteLine($"ROSLYN DOCUMENTATION: {count} declarations; {failures.Count} errors.");
        return failures.Count == 0 ? 0 : 1;
    }

    /// <summary>Identifies every explicitly named declaration that must carry a summary.</summary>
    /// <param name="node">A non-trivia C# syntax node from an SDK parser.</param>
    /// <returns>True for named types, callables, fields, properties, events, indexers and enum values.</returns>
    private static bool RequiresSummary(SyntaxNode node)
        => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax or BaseMethodDeclarationSyntax
            or LocalFunctionStatementSyntax or PropertyDeclarationSyntax or IndexerDeclarationSyntax
            or FieldDeclarationSyntax or EventDeclarationSyntax or EventFieldDeclarationSyntax or EnumMemberDeclarationSyntax;
}
