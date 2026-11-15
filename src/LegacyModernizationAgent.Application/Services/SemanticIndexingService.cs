using LegacyModernizationAgent.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace LegacyModernizationAgent.Application.Services;

public interface ISemanticIndexingService
{
    /// <summary>İş için semantik indeks oluşturur; embedding/Qdrant yoksa false döner (akışı bozmaz).</summary>
    Task<bool> IndexJobAsync(Guid jobId, CancellationToken ct = default);
}

/// <summary>
/// Semantik indeksleme (CLAUDE.md 4.2): class özet metinleri embedding'e çevrilip
/// Qdrant'a yazılır. Payload: analysisJobId, filePath, className, findingIds.
/// </summary>
public class SemanticIndexingService(
    IFindingRepository findings,
    IEmbeddingClient embeddings,
    IVectorStore vectorStore,
    ILogger<SemanticIndexingService> logger) : ISemanticIndexingService
{
    private const int BatchSize = 64;

    public async Task<bool> IndexJobAsync(Guid jobId, CancellationToken ct = default)
    {
        if (!embeddings.IsConfigured)
        {
            logger.LogInformation("Embedding istemcisi yapılandırılmamış; indeksleme atlanıyor.");
            return false;
        }

        if (!await vectorStore.IsAvailableAsync(ct))
        {
            logger.LogWarning("Qdrant erişilebilir değil; indeksleme atlanıyor. (docker run -p 6333:6333 qdrant/qdrant)");
            return false;
        }

        var summaries = await findings.GetClassSummariesAsync(jobId, ct);
        if (summaries.Count == 0)
            return false;

        var jobFindings = await findings.GetByJobAsync(jobId, ct);
        var findingIdsByClass = jobFindings
            .Where(f => !string.IsNullOrEmpty(f.ClassName))
            .GroupBy(f => ClassKey.Of(f.ClassName!, f.FilePath))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(f => f.Id).ToList());

        await vectorStore.EnsureCollectionAsync(embeddings.VectorSize, ct);

        foreach (var batch in summaries.Chunk(BatchSize))
        {
            var vectors = await embeddings.EmbedAsync(batch.Select(s => s.SummaryText).ToList(), ct);

            var points = batch.Select((summary, i) => new VectorPoint(
                summary.Id,
                vectors[i],
                jobId,
                summary.ClassName,
                summary.FilePath,
                summary.SummaryText,
                findingIdsByClass.TryGetValue(ClassKey.Of(summary.ClassName, summary.FilePath), out var ids) ? ids : [])).ToList();

            await vectorStore.UpsertAsync(points, ct);
        }

        logger.LogInformation("[{JobId}] {Count} sınıf semantik indekse yazıldı.", jobId, summaries.Count);
        return true;
    }
}
