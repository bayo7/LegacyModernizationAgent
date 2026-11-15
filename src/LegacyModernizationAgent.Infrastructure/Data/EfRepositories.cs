using LegacyModernizationAgent.Core.Abstractions;
using LegacyModernizationAgent.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace LegacyModernizationAgent.Infrastructure.Data;

public class EfAnalysisJobRepository(AppDbContext db) : IAnalysisJobRepository
{
    public async Task<AnalysisJob> CreateAsync(AnalysisJob job, CancellationToken ct = default)
    {
        db.AnalysisJobs.Add(job);
        await db.SaveChangesAsync(ct);
        return job;
    }

    public async Task<AnalysisJob?> GetAsync(Guid id, CancellationToken ct = default) =>
        await db.AnalysisJobs.FirstOrDefaultAsync(j => j.Id == id, ct);

    public async Task<IReadOnlyList<AnalysisJob>> ListAsync(CancellationToken ct = default) =>
        await db.AnalysisJobs.AsNoTracking().OrderByDescending(j => j.CreatedAt).ToListAsync(ct);

    public async Task UpdateAsync(AnalysisJob job, CancellationToken ct = default)
    {
        if (db.Entry(job).State == EntityState.Detached)
            db.AnalysisJobs.Update(job);

        await db.SaveChangesAsync(ct);
    }
}

public class EfFindingRepository(AppDbContext db) : IFindingRepository
{
    public async Task SaveRangeAsync(IEnumerable<Finding> findings, CancellationToken ct = default)
    {
        db.Findings.AddRange(findings);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Finding>> GetByJobAsync(Guid jobId, CancellationToken ct = default) =>
        await db.Findings.AsNoTracking()
            .Where(f => f.AnalysisJobId == jobId)
            .OrderBy(f => f.RuleName).ThenBy(f => f.FilePath).ThenBy(f => f.LineNumber)
            .ToListAsync(ct);

    public async Task SaveDependenciesAsync(IEnumerable<ClassDependency> dependencies, CancellationToken ct = default)
    {
        db.ClassDependencies.AddRange(dependencies);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ClassDependency>> GetDependenciesAsync(Guid jobId, CancellationToken ct = default) =>
        await db.ClassDependencies.AsNoTracking()
            .Where(d => d.AnalysisJobId == jobId)
            .ToListAsync(ct);

    public async Task SaveClassSummariesAsync(IEnumerable<ClassSummary> summaries, CancellationToken ct = default)
    {
        db.ClassSummaries.AddRange(summaries);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ClassSummary>> GetClassSummariesAsync(Guid jobId, CancellationToken ct = default) =>
        await db.ClassSummaries.AsNoTracking()
            .Where(s => s.AnalysisJobId == jobId)
            .OrderBy(s => s.Namespace).ThenBy(s => s.ClassName)
            .ToListAsync(ct);
}

public class EfReportRepository(AppDbContext db) : IReportRepository
{
    public async Task SaveAsync(Report report, CancellationToken ct = default)
    {
        db.Reports.Add(report);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Report?> GetLatestAsync(Guid jobId, CancellationToken ct = default) =>
        await db.Reports.AsNoTracking()
            .Where(r => r.AnalysisJobId == jobId)
            .OrderByDescending(r => r.GeneratedAt)
            .FirstOrDefaultAsync(ct);
}

public class EfChatRepository(AppDbContext db) : IChatRepository
{
    public async Task AddAsync(ChatMessage message, CancellationToken ct = default)
    {
        db.ChatMessages.Add(message);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetByJobAsync(Guid jobId, CancellationToken ct = default) =>
        await db.ChatMessages.AsNoTracking()
            .Where(m => m.AnalysisJobId == jobId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);
}
