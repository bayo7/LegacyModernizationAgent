using System.Text.Json;
using LegacyModernizationAgent.Analyzer.Engine;
using LegacyModernizationAgent.Core.Abstractions;
using LegacyModernizationAgent.Core.Entities;
using Microsoft.Extensions.Logging;
using CoreEntities = LegacyModernizationAgent.Core.Entities;

namespace LegacyModernizationAgent.Application.Services;

public interface IAnalysisPipeline
{
    Task RunAsync(Guid jobId, CancellationToken ct = default);
}

/// <summary>
/// Uçtan uca analiz akışı: Queued → Analyzing (Roslyn) → Indexing (embedding/Qdrant)
/// → Reporting (LLM) → Completed. Indexing ve Reporting adımları yapılandırma yoksa
/// akışı durdurmaz — sistem elindeki katmanlarla en iyi çıktıyı üretir.
/// </summary>
public class AnalysisPipeline(
    IAnalysisJobRepository jobs,
    IFindingRepository findings,
    ISourcePreparer sourcePreparer,
    ISemanticIndexingService indexing,
    IReportGenerationService reporting,
    ILogger<AnalysisPipeline> logger) : IAnalysisPipeline
{
    public async Task RunAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
        {
            logger.LogWarning("Analiz işi bulunamadı: {JobId}", jobId);
            return;
        }

        try
        {
            await SetStatusAsync(job, JobStatus.Analyzing, ct);

            var sourcePath = await sourcePreparer.PrepareAsync(job.RepoUrlOrPath, job.Id, ct);
            var targetPath = Directory.Exists(sourcePath)
                ? MsBuildSolutionLoader.FindSolutionOrProject(sourcePath)
                    ?? throw new InvalidOperationException($"'{sourcePath}' içinde .sln veya .csproj bulunamadı.")
                : sourcePath;

            logger.LogInformation("Roslyn analizi başlıyor: {Path}", targetPath);
            var result = await MsBuildSolutionLoader.AnalyzeAsync(
                targetPath,
                progress: new Progress<string>(m => logger.LogInformation("[{JobId}] {Message}", jobId, m)),
                cancellationToken: ct);

            foreach (var diagnostic in result.Diagnostics)
                logger.LogWarning("[{JobId}] Analiz tanısı: {Diagnostic}", jobId, diagnostic);

            var findingEntities = result.Findings.Select(f => new CoreEntities.Finding
            {
                AnalysisJobId = job.Id,
                RuleName = f.RuleName,
                Severity = f.Severity.ToString(),
                Confidence = f.Confidence.ToString(),
                FilePath = f.FilePath,
                LineNumber = f.LineNumber,
                ClassName = f.ClassName,
                Description = f.Description,
                LimitationNote = f.LimitationNote,
                MetricsJson = f.Metrics is null ? null : JsonSerializer.Serialize(f.Metrics)
            }).ToList();

            await findings.SaveRangeAsync(findingEntities, ct);

            await findings.SaveDependenciesAsync(result.Dependencies.Select(d => new ClassDependency
            {
                AnalysisJobId = job.Id,
                FromClass = d.FromClass,
                ToClass = d.ToClass
            }), ct);

            var findingsByClass = findingEntities
                .Where(f => !string.IsNullOrEmpty(f.ClassName))
                .GroupBy(f => ClassKey.Of(f.ClassName!, f.FilePath))
                .ToDictionary(g => g.Key, g => (IReadOnlyList<CoreEntities.Finding>)g.ToList());

            await findings.SaveClassSummariesAsync(result.Classes.Select(c => new CoreEntities.ClassSummary
            {
                AnalysisJobId = job.Id,
                ClassName = c.ClassName,
                Namespace = c.Namespace,
                FilePath = c.FilePath,
                SummaryText = SummaryTextBuilder.Build(
                    c,
                    findingsByClass.TryGetValue(ClassKey.Of(c.ClassName, c.FilePath), out var classFindings) ? classFindings : [])
            }), ct);

            logger.LogInformation(
                "[{JobId}] Roslyn analizi bitti: {FindingCount} bulgu, {ClassCount} sınıf.",
                jobId, findingEntities.Count, result.Classes.Count);

            await SetStatusAsync(job, JobStatus.Indexing, ct);
            try
            {
                var indexed = await indexing.IndexJobAsync(job.Id, ct);
                if (!indexed)
                    logger.LogInformation("[{JobId}] Semantik indeksleme atlandı (embedding/Qdrant yapılandırılmamış).", jobId);
            }
            catch (Exception ex)
            {
                // İndeksleme opsiyonel bir zenginleştirmedir: embedding/Qdrant çalışma
                // zamanında da hata verebilir; analiz sonuçları ve rapor bundan bağımsız üretilmeli.
                logger.LogWarning(ex, "[{JobId}] Semantik indeksleme başarısız oldu; rapor üretimine devam ediliyor.", jobId);
            }

            await SetStatusAsync(job, JobStatus.Reporting, ct);
            await reporting.GenerateAsync(job.Id, ct);

            job.CompletedAt = DateTime.UtcNow;
            await SetStatusAsync(job, JobStatus.Completed, ct);
            logger.LogInformation("[{JobId}] Analiz tamamlandı.", jobId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{JobId}] Analiz başarısız oldu.", jobId);
            job.ErrorMessage = ex.Message;
            job.CompletedAt = DateTime.UtcNow;
            await SetStatusAsync(job, JobStatus.Failed, CancellationToken.None);
        }
        finally
        {
            // Klon/zip çalışma alanı yalnızca analiz için gerekli; sonuçlar DB'de.
            // Silinmezse her analiz temp klasöründe bir repo kopyası biriktirir.
            sourcePreparer.Cleanup(job.Id);
        }
    }

    private async Task SetStatusAsync(AnalysisJob job, JobStatus status, CancellationToken ct)
    {
        job.Status = status;
        await jobs.UpdateAsync(job, ct);
    }
}
