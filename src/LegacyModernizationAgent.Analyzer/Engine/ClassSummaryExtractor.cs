using System.Xml.Linq;
using LegacyModernizationAgent.Analyzer.Graph;
using LegacyModernizationAgent.Analyzer.Models;
using Microsoft.CodeAnalysis;

namespace LegacyModernizationAgent.Analyzer.Engine;

/// <summary>
/// Semantik indeksleme (Faz 3) için class özetleri çıkarır: embedding'e gönderilecek
/// metin bu özetlerden üretilir (bkz. CLAUDE.md 4.2 şablonu).
/// </summary>
public static class ClassSummaryExtractor
{
    public static IEnumerable<ClassSummary> Extract(Compilation compilation)
    {
        foreach (var classSymbol in DependencyGraphBuilder.GetSourceClasses(compilation))
        {
            var location = classSymbol.Locations.FirstOrDefault(l => l.IsInSource);
            if (location is null)
                continue;

            var filePath = location.GetLineSpan().Path;
            if (GeneratedCodeDetector.IsGeneratedFile(filePath))
                continue;

            var signatures = classSymbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Ordinary && m.DeclaredAccessibility == Accessibility.Public)
                .Select(m => m.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
                .ToList();

            yield return new ClassSummary(
                classSymbol.Name,
                classSymbol.ContainingNamespace.IsGlobalNamespace ? "" : classSymbol.ContainingNamespace.ToDisplayString(),
                filePath,
                signatures,
                ExtractXmlSummary(classSymbol));
        }
    }

    private static string? ExtractXmlSummary(INamedTypeSymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            return XDocument.Parse(xml).Descendants("summary").FirstOrDefault()?.Value.Trim();
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }
}
