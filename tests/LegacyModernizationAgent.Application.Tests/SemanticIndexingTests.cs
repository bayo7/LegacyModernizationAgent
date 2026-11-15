using LegacyModernizationAgent.Application.Services;
using LegacyModernizationAgent.Core.Abstractions;
using LegacyModernizationAgent.Core.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace LegacyModernizationAgent.Application.Tests;

public class SemanticIndexingTests
{
    private sealed class FixedEmbeddingClient : IEmbeddingClient
    {
        public bool IsConfigured => true;
        public int VectorSize => 3;

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new float[] { 1f, 0f, 0f }).ToList());
    }

    private sealed class RecordingVectorStore : IVectorStore
    {
        public List<VectorPoint> Points { get; } = [];

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task EnsureCollectionAsync(int vectorSize, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpsertAsync(IReadOnlyList<VectorPoint> points, CancellationToken ct = default)
        {
            Points.AddRange(points);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(Guid analysisJobId, float[] queryVector, int topK, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VectorSearchHit>>([]);
    }

    [Fact]
    public async Task Ayni_adli_siniflarin_bulgulari_birbirine_karismaz()
    {
        var jobId = Guid.NewGuid();
        var repository = new FakeFindingRepository();

        // Farklı namespace'lerde (dolayısıyla farklı dosyalarda) iki 'Helper' sınıfı.
        repository.Summaries.AddRange(
        [
            new ClassSummary { AnalysisJobId = jobId, ClassName = "Helper", Namespace = "Shop.Billing", FilePath = "Billing/Helper.cs", SummaryText = "Class: Helper (Billing)" },
            new ClassSummary { AnalysisJobId = jobId, ClassName = "Helper", Namespace = "Shop.Shipping", FilePath = "Shipping/Helper.cs", SummaryText = "Class: Helper (Shipping)" }
        ]);

        var billingFinding = new Finding { AnalysisJobId = jobId, RuleName = "GodClass", ClassName = "Helper", FilePath = "Billing/Helper.cs" };
        var shippingFinding = new Finding { AnalysisJobId = jobId, RuleName = "LongMethod", ClassName = "Helper", FilePath = "Shipping/Helper.cs" };
        repository.Findings.AddRange([billingFinding, shippingFinding]);

        var store = new RecordingVectorStore();
        var service = new SemanticIndexingService(repository, new FixedEmbeddingClient(), store, NullLogger<SemanticIndexingService>.Instance);

        var indexed = await service.IndexJobAsync(jobId);

        Assert.True(indexed);
        Assert.Equal(2, store.Points.Count);

        var billingPoint = Assert.Single(store.Points, p => p.FilePath == "Billing/Helper.cs");
        Assert.Equal(billingFinding.Id, Assert.Single(billingPoint.FindingIds));

        var shippingPoint = Assert.Single(store.Points, p => p.FilePath == "Shipping/Helper.cs");
        Assert.Equal(shippingFinding.Id, Assert.Single(shippingPoint.FindingIds));
    }

    [Fact]
    public async Task Embedding_yapilandirilmamissa_indeksleme_atlanir()
    {
        var service = new SemanticIndexingService(
            new FakeFindingRepository(), new UnconfiguredEmbeddingClient(), new UnavailableVectorStore(),
            NullLogger<SemanticIndexingService>.Instance);

        Assert.False(await service.IndexJobAsync(Guid.NewGuid()));
    }
}
