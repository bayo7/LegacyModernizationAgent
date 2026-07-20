namespace LegacyModernizationAgent.Core.Entities;

/// <summary>
/// Kalıcılaştırılmış analiz bulgusu (bkz. CLAUDE.md Bölüm 5 DDL).
/// Severity/Confidence DB'de string tutulur — raporlama araçlarında okunabilirlik için.
/// </summary>
public class Finding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnalysisJobId { get; set; }
    public string RuleName { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Confidence { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int LineNumber { get; set; }
    public string? ClassName { get; set; }
    public string Description { get; set; } = "";
    public string? LimitationNote { get; set; }
    public string? MetricsJson { get; set; }
}
