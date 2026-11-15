using LegacyModernizationAgent.Core.Abstractions;
using LegacyModernizationAgent.Core.Entities;

namespace LegacyModernizationAgent.Application.Tests;

// Servis testleri için bellek içi sahteler: DB, LLM, embedding ve Qdrant olmadan
// "hiçbir şey yapılandırılmamış" (fallback) davranışı doğrulanır.

internal sealed class FakeFindingRepository : IFindingRepository
{
    public List<Finding> Findings { get; } = [];
    public List<ClassDependency> Dependencies { get; } = [];
    public List<ClassSummary> Summaries { get; } = [];

    public Task SaveRangeAsync(IEnumerable<Finding> findings, CancellationToken ct = default)
    {
        Findings.AddRange(findings);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Finding>> GetByJobAsync(Guid jobId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Finding>>(Findings.Where(f => f.AnalysisJobId == jobId).ToList());

    public Task SaveDependenciesAsync(IEnumerable<ClassDependency> dependencies, CancellationToken ct = default)
    {
        Dependencies.AddRange(dependencies);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ClassDependency>> GetDependenciesAsync(Guid jobId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ClassDependency>>(Dependencies.Where(d => d.AnalysisJobId == jobId).ToList());

    public Task SaveClassSummariesAsync(IEnumerable<ClassSummary> summaries, CancellationToken ct = default)
    {
        Summaries.AddRange(summaries);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ClassSummary>> GetClassSummariesAsync(Guid jobId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ClassSummary>>(Summaries.Where(s => s.AnalysisJobId == jobId).ToList());
}

internal sealed class FakeReportRepository : IReportRepository
{
    public List<Report> Reports { get; } = [];

    public Task SaveAsync(Report report, CancellationToken ct = default)
    {
        Reports.Add(report);
        return Task.CompletedTask;
    }

    public Task<Report?> GetLatestAsync(Guid jobId, CancellationToken ct = default) =>
        Task.FromResult(Reports.Where(r => r.AnalysisJobId == jobId).OrderByDescending(r => r.GeneratedAt).FirstOrDefault());
}

internal sealed class FakeChatRepository : IChatRepository
{
    public List<ChatMessage> Messages { get; } = [];

    public Task AddAsync(ChatMessage message, CancellationToken ct = default)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ChatMessage>> GetByJobAsync(Guid jobId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.Where(m => m.AnalysisJobId == jobId).ToList());
}

internal sealed class UnconfiguredLlmClient : ILlmClient
{
    public bool IsConfigured => false;

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default) =>
        throw new InvalidOperationException("Yapılandırılmamış LLM çağrılmamalıydı.");
}

internal sealed class UnconfiguredEmbeddingClient : IEmbeddingClient
{
    public bool IsConfigured => false;
    public int VectorSize => 0;

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
        throw new InvalidOperationException("Yapılandırılmamış embedding istemcisi çağrılmamalıydı.");
}

internal sealed class UnavailableVectorStore : IVectorStore
{
    public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(false);

    public Task EnsureCollectionAsync(int vectorSize, CancellationToken ct = default) =>
        throw new InvalidOperationException("Erişilemeyen vektör deposu çağrılmamalıydı.");

    public Task UpsertAsync(IReadOnlyList<VectorPoint> points, CancellationToken ct = default) =>
        throw new InvalidOperationException("Erişilemeyen vektör deposu çağrılmamalıydı.");

    public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(Guid analysisJobId, float[] queryVector, int topK, CancellationToken ct = default) =>
        throw new InvalidOperationException("Erişilemeyen vektör deposu çağrılmamalıydı.");
}
