using LegacyModernizationAgent.Analyzer.Models;
using Microsoft.CodeAnalysis;

namespace LegacyModernizationAgent.Analyzer.Abstractions;

/// <summary>
/// Solution/workspace seviyesinde bütünsel bakış gerektiren kurallar için arayüz
/// (Circular Dependency, Kullanılmayan Kod). Tek dosyaya bakarak bu kurallar
/// çalıştırılamaz — ör. SymbolFinder.FindReferencesAsync tüm solution'ı tarar.
/// </summary>
public interface ISolutionAnalyzer
{
    string RuleName { get; }
    Severity DefaultSeverity { get; }
    Task<IReadOnlyList<Finding>> AnalyzeAsync(Solution solution, CancellationToken cancellationToken = default);
}
