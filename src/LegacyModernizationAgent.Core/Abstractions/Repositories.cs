using LegacyModernizationAgent.Core.Entities;

namespace LegacyModernizationAgent.Core.Abstractions;

public interface IAnalysisJobRepository
{
    Task<AnalysisJob> CreateAsync(AnalysisJob job, CancellationToken ct = default);
    Task<AnalysisJob?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<AnalysisJob>> ListAsync(CancellationToken ct = default);
    Task UpdateAsync(AnalysisJob job, CancellationToken ct = default);
}

public interface IFindingRepository
{
    Task SaveRangeAsync(IEnumerable<Finding> findings, CancellationToken ct = default);
    Task<IReadOnlyList<Finding>> GetByJobAsync(Guid jobId, CancellationToken ct = default);
    Task SaveDependenciesAsync(IEnumerable<ClassDependency> dependencies, CancellationToken ct = default);
    Task<IReadOnlyList<ClassDependency>> GetDependenciesAsync(Guid jobId, CancellationToken ct = default);
    Task SaveClassSummariesAsync(IEnumerable<ClassSummary> summaries, CancellationToken ct = default);
    Task<IReadOnlyList<ClassSummary>> GetClassSummariesAsync(Guid jobId, CancellationToken ct = default);
}

public interface IReportRepository
{
    Task SaveAsync(Report report, CancellationToken ct = default);
    Task<Report?> GetLatestAsync(Guid jobId, CancellationToken ct = default);
}

public interface IChatRepository
{
    Task AddAsync(ChatMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> GetByJobAsync(Guid jobId, CancellationToken ct = default);
}
