using LegacyModernizationAgent.Core.Entities;

namespace LegacyModernizationAgent.Application.Dtos;

/// <summary>Yeni analiz isteği: lokal yol, git URL'i olabilir (zip upload ayrı endpoint'ten gelir).</summary>
public record StartAnalysisRequest(string Source);

public record AnalysisJobDto(
    Guid Id,
    string RepoUrlOrPath,
    string Status,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    string? ErrorMessage)
{
    public static AnalysisJobDto From(AnalysisJob job) => new(
        job.Id, job.RepoUrlOrPath, job.Status.ToString(), job.CreatedAt, job.CompletedAt, job.ErrorMessage);
}

public record FindingDto(
    Guid Id,
    string RuleName,
    string Severity,
    string Confidence,
    string FilePath,
    int LineNumber,
    string? ClassName,
    string Description,
    string? LimitationNote,
    string? MetricsJson)
{
    public static FindingDto From(Finding f) => new(
        f.Id, f.RuleName, f.Severity, f.Confidence, f.FilePath, f.LineNumber,
        f.ClassName, f.Description, f.LimitationNote, f.MetricsJson);
}

public record DependencyDto(string FromClass, string ToClass);

public record ChatRequest(string Message);

public record ChatMessageDto(string Role, string Content, DateTime CreatedAt);

public record ChatResponse(string Answer, IReadOnlyList<string> SourceFiles);
