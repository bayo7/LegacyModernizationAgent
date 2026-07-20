namespace LegacyModernizationAgent.Core.Abstractions;

/// <summary>
/// LLM tamamlama istemcisi. IsConfigured=false ise sistem LLM'siz fallback
/// davranışına düşer (rapor: kural tabanlı önceliklendirme; chat: anahtar kelime araması) —
/// API anahtarı olmadan da uçtan uca demo çalışabilsin diye bilinçli tasarım kararı.
/// </summary>
public interface ILlmClient
{
    bool IsConfigured { get; }
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}

public interface IEmbeddingClient
{
    bool IsConfigured { get; }
    int VectorSize { get; }
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}

/// <summary>Qdrant'a yazılacak nokta (bkz. CLAUDE.md 4.2 — payload alanları).</summary>
public record VectorPoint(
    Guid Id,
    float[] Vector,
    Guid AnalysisJobId,
    string ClassName,
    string FilePath,
    string SummaryText,
    IReadOnlyList<Guid> FindingIds);

public record VectorSearchHit(
    string ClassName,
    string FilePath,
    string SummaryText,
    double Score,
    IReadOnlyList<Guid> FindingIds);

public interface IVectorStore
{
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    Task EnsureCollectionAsync(int vectorSize, CancellationToken ct = default);
    Task UpsertAsync(IReadOnlyList<VectorPoint> points, CancellationToken ct = default);
    Task<IReadOnlyList<VectorSearchHit>> SearchAsync(Guid analysisJobId, float[] queryVector, int topK, CancellationToken ct = default);
}

/// <summary>
/// Analiz işlerini arka plana atan kuyruk. Faz 2'de Channel tabanlı IHostedService,
/// Faz 3'te Hangfire implementasyonu (kalıcı kuyruk + otomatik retry) kullanılır.
/// </summary>
public interface IAnalysisQueue
{
    Task EnqueueAsync(Guid jobId, CancellationToken ct = default);
}
