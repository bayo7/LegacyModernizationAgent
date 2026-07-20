namespace LegacyModernizationAgent.Core.Entities;

public class Report
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnalysisJobId { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string ReportJson { get; set; } = "";
}
