namespace LegacyModernizationAgent.Core.Entities;

public class ClassDependency
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnalysisJobId { get; set; }
    public string FromClass { get; set; } = "";
    public string ToClass { get; set; } = "";
}
