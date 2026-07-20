namespace LegacyModernizationAgent.Analyzer.Models;

/// <summary>
/// Bir solution'ın uçtan uca analizinin toplam çıktısı.
/// </summary>
public record AnalysisResult(
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<DependencyEdge> Dependencies,
    IReadOnlyList<ClassSummary> Classes,
    IReadOnlyList<string> Diagnostics
);

/// <summary>
/// Semantik indeksleme katmanının (Faz 3) embedding'e göndereceği class özeti.
/// Analyzer katmanında üretilir ki Roslyn bağımlılığı üst katmanlara sızmasın.
/// </summary>
public record ClassSummary(
    string ClassName,
    string Namespace,
    string FilePath,
    IReadOnlyList<string> PublicMethodSignatures,
    string? XmlDocSummary
);
