using LegacyModernizationAgent.Analyzer.Abstractions;
using LegacyModernizationAgent.Analyzer.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LegacyModernizationAgent.Analyzer.Rules;

/// <summary>
/// Uzun Metod kuralı. Satır sayısı tek başına zayıf bir sinyal olduğu için
/// Cyclomatic Complexity ile birlikte değerlendirilir:
/// uzun + karmaşık → High, uzun ama düz (ör. switch-case mapping) → Low,
/// kısa ama çok karmaşık → yine bulgu (iç içe koşullar satır sayısına yansımayabilir).
/// </summary>
public sealed class LongMethodAnalyzer(AnalyzerThresholds? thresholds = null) : ICodeAnalyzer
{
    private readonly AnalyzerThresholds _thresholds = thresholds ?? AnalyzerThresholds.Default;

    public string RuleName => "LongMethod";
    public Severity DefaultSeverity => Severity.Medium;

    private const string Limitation =
        "Bu bulgu satır sayısı ve cyclomatic complexity sezgisellerine dayanır; " +
        "otomatik doğruluk garantisi taşımaz. Uzun ama düz akışlı metodlar (ör. mapping) sorun olmayabilir.";

    public IEnumerable<Finding> Analyze(SyntaxNode root, SemanticModel semanticModel, string filePath)
    {
        if (GeneratedCodeDetector.IsGeneratedFile(filePath))
            yield break;

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var lineSpan = method.GetLocation().GetLineSpan();
            var lineCount = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;
            var complexity = CalculateCyclomaticComplexity(method);

            var isLong = lineCount >= _thresholds.LongMethodLineCount;
            var isVeryComplex = complexity >= _thresholds.HighComplexityThreshold;

            if (!isLong && !isVeryComplex)
                continue;

            var severity = (isLong, complexity) switch
            {
                (true, var c) when c >= _thresholds.HighComplexityThreshold => Severity.High,
                (true, var c) when c <= _thresholds.LowComplexityThreshold => Severity.Low,
                (true, _) => Severity.Medium,
                // kısa ama complexity yüksek: satır metriği yanıltıcı, karmaşıklık esas sinyal
                (false, _) => Severity.Medium
            };

            var className = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text ?? "";

            var description = isLong
                ? $"'{method.Identifier.Text}' metodu {lineCount} satır ve cyclomatic complexity {complexity}. " +
                  "Daha küçük, tek sorumluluklu metodlara bölünmesi incelemeye değer."
                : $"'{method.Identifier.Text}' metodu kısa ({lineCount} satır) ama cyclomatic complexity {complexity} ile " +
                  "yoğun dallanma içeriyor. Koşul mantığının sadeleştirilmesi kontrol edilmesi önerilir.";

            yield return new Finding(
                RuleName,
                severity,
                Confidence.High, // metrikler deterministik ölçülür; yorumu Confidence değil severity taşır
                filePath,
                lineSpan.StartLinePosition.Line + 1,
                className,
                description,
                Limitation,
                new Dictionary<string, object>
                {
                    ["LineCount"] = lineCount,
                    ["CyclomaticComplexity"] = complexity
                });
        }
    }

    /// <summary>
    /// Karar noktası sayısı + 1: if / else if / for / foreach / while / do /
    /// case label / switch expression arm / catch / ternary / &amp;&amp; / || / ??
    /// </summary>
    internal static int CalculateCyclomaticComplexity(MethodDeclarationSyntax method)
    {
        var complexity = 1;
        SyntaxNode? body = (SyntaxNode?)method.Body ?? method.ExpressionBody;
        if (body is null)
            return complexity;

        foreach (var node in body.DescendantNodes())
        {
            switch (node)
            {
                case IfStatementSyntax:
                case ForStatementSyntax:
                case ForEachStatementSyntax:
                case WhileStatementSyntax:
                case DoStatementSyntax:
                case CaseSwitchLabelSyntax:
                case CasePatternSwitchLabelSyntax:
                case SwitchExpressionArmSyntax:
                case CatchClauseSyntax:
                case ConditionalExpressionSyntax:
                    complexity++;
                    break;
                case BinaryExpressionSyntax binary when
                    binary.IsKind(SyntaxKind.LogicalAndExpression) ||
                    binary.IsKind(SyntaxKind.LogicalOrExpression) ||
                    binary.IsKind(SyntaxKind.CoalesceExpression):
                    complexity++;
                    break;
            }
        }

        return complexity;
    }
}
