using LegacyModernizationAgent.Analyzer.Abstractions;
using LegacyModernizationAgent.Analyzer.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LegacyModernizationAgent.Analyzer.Rules;

/// <summary>
/// God Class kuralı: 15+ public metod VEYA 400+ satır.
/// Zorunlu istisnalar: *.Designer.cs, Migrations, [GeneratedCode].
/// Controller'lar için eşik yükseltilir (çok endpoint barındırmaları doğaldır).
/// </summary>
public sealed class GodClassAnalyzer(AnalyzerThresholds? thresholds = null) : ICodeAnalyzer
{
    private readonly AnalyzerThresholds _thresholds = thresholds ?? AnalyzerThresholds.Default;

    public string RuleName => "GodClass";
    public Severity DefaultSeverity => Severity.Medium;

    private const string Limitation =
        "Metod/satır sayısı sezgisel bir sinyaldir; DTO taşıyıcıları veya bilinçli olarak geniş tutulan " +
        "facade sınıfları yanlış pozitif olabilir. Sınıfın sorumluluk sayısına insan gözüyle bakılması önerilir.";

    public IEnumerable<Finding> Analyze(SyntaxNode root, SemanticModel semanticModel, string filePath)
    {
        if (GeneratedCodeDetector.IsGeneratedFile(filePath))
            yield break;

        foreach (var classDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (GeneratedCodeDetector.HasGeneratedCodeAttribute(classDeclaration, semanticModel))
                continue;

            var publicMethodCount = classDeclaration.Members
                .OfType<MethodDeclarationSyntax>()
                .Count(m => m.Modifiers.Any(SyntaxKind.PublicKeyword));

            var lineSpan = classDeclaration.GetLocation().GetLineSpan();
            var lineCount = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;

            var isController = IsController(classDeclaration, semanticModel);
            var methodThreshold = isController ? _thresholds.GodClassControllerPublicMethodCount : _thresholds.GodClassPublicMethodCount;
            var lineThreshold = isController ? _thresholds.GodClassControllerLineCount : _thresholds.GodClassLineCount;

            if (publicMethodCount < methodThreshold && lineCount < lineThreshold)
                continue;

            // Her iki eşik birden aşılmışsa sinyal güçlenir.
            var severity = publicMethodCount >= methodThreshold && lineCount >= lineThreshold
                ? Severity.High
                : Severity.Medium;

            var className = classDeclaration.Identifier.Text;

            yield return new Finding(
                RuleName,
                severity,
                Confidence.Medium,
                filePath,
                lineSpan.StartLinePosition.Line + 1,
                className,
                $"'{className}' sınıfı {publicMethodCount} public metod ve {lineCount} satır içeriyor" +
                (isController ? " (controller eşiği uygulandı)" : "") +
                ". Birden fazla sorumluluk taşıyor olabilir; daha küçük sınıflara ayrıştırılması incelemeye değer.",
                Limitation,
                new Dictionary<string, object>
                {
                    ["PublicMethodCount"] = publicMethodCount,
                    ["LineCount"] = lineCount,
                    ["IsController"] = isController
                });
        }
    }

    private static bool IsController(ClassDeclarationSyntax classDeclaration, SemanticModel semanticModel)
    {
        // İsim konvansiyonu hızlı yol; semantic model ile base type kontrolü isim
        // konvansiyonuna uymayan controller'ları da yakalar.
        if (classDeclaration.Identifier.Text.EndsWith("Controller", StringComparison.Ordinal))
            return true;

        var symbol = semanticModel.GetDeclaredSymbol(classDeclaration);
        for (var baseType = symbol?.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.Name is "ControllerBase" or "Controller" or "ApiController")
                return true;
        }

        return false;
    }
}
