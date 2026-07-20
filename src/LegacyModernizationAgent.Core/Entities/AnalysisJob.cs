namespace LegacyModernizationAgent.Core.Entities;

public enum JobStatus
{
    Queued,
    Analyzing,
    Indexing,
    Reporting,
    Completed,
    Failed
}

public class AnalysisJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RepoUrlOrPath { get; set; } = "";
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    /// <summary>DDL'e ek alan: Failed durumunda kullanıcıya gösterilecek neden.</summary>
    public string? ErrorMessage { get; set; }
}
