using LegacyModernizationAgent.Analyzer.Models;
using Microsoft.CodeAnalysis;

namespace LegacyModernizationAgent.Analyzer.Abstractions;

/// <summary>
/// Dosya (syntax tree) seviyesinde çalışan analiz kuralı. Her kural bağımsız ve
/// test edilebilirdir; yeni kural eklemek mevcut kodu değiştirmez (Open/Closed).
/// </summary>
public interface ICodeAnalyzer
{
    string RuleName { get; }
    Severity DefaultSeverity { get; }
    IEnumerable<Finding> Analyze(SyntaxNode root, SemanticModel semanticModel, string filePath);
}
