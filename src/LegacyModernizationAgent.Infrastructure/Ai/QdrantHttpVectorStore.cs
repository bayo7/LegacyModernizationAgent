using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using LegacyModernizationAgent.Core.Abstractions;
using Microsoft.Extensions.Options;

namespace LegacyModernizationAgent.Infrastructure.Ai;

public class QdrantOptions
{
    public const string SectionName = "Qdrant";

    public string Url { get; set; } = "http://localhost:6333";
    public string Collection { get; set; } = "code_embeddings";
}

/// <summary>
/// Qdrant REST API istemcisi (gRPC istemcisi yerine HttpClient — ekstra bağımlılık yok,
/// ihtiyaç duyulan 4 uç nokta basit). Koleksiyon: code_embeddings; payload:
/// analysisJobId, filePath, className, findingIds (CLAUDE.md 4.2).
/// </summary>
public class QdrantHttpVectorStore(HttpClient http, IOptions<QdrantOptions> options) : IVectorStore
{
    private readonly QdrantOptions _options = options.Value;

    private string CollectionUrl => $"{_options.Url.TrimEnd('/')}/collections/{_options.Collection}";

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            var response = await http.GetAsync($"{_options.Url.TrimEnd('/')}/collections", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    public async Task EnsureCollectionAsync(int vectorSize, CancellationToken ct = default)
    {
        var existing = await http.GetAsync(CollectionUrl, ct);
        if (existing.StatusCode == HttpStatusCode.OK)
            return;

        var create = await http.PutAsJsonAsync(CollectionUrl, new
        {
            vectors = new { size = vectorSize, distance = "Cosine" }
        }, ct);
        create.EnsureSuccessStatusCode();
    }

    public async Task UpsertAsync(IReadOnlyList<VectorPoint> points, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"{CollectionUrl}/points?wait=true", new
        {
            points = points.Select(p => new
            {
                id = p.Id,
                vector = p.Vector,
                payload = new
                {
                    analysisJobId = p.AnalysisJobId,
                    className = p.ClassName,
                    filePath = p.FilePath,
                    summaryText = p.SummaryText,
                    findingIds = p.FindingIds
                }
            })
        }, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Qdrant upsert {(int)response.StatusCode}: {body}");
        }
    }

    public async Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        Guid analysisJobId, float[] queryVector, int topK, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync($"{CollectionUrl}/points/search", new
        {
            vector = queryVector,
            limit = topK,
            with_payload = true,
            filter = new
            {
                must = new object[]
                {
                    new { key = "analysisJobId", match = new { value = analysisJobId } }
                }
            }
        }, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Qdrant search {(int)response.StatusCode}: {body}");

        var results = JsonNode.Parse(body)?["result"] as JsonArray ?? [];
        return results.Select(hit =>
        {
            var payload = hit!["payload"]!;
            return new VectorSearchHit(
                payload["className"]?.GetValue<string>() ?? "",
                payload["filePath"]?.GetValue<string>() ?? "",
                payload["summaryText"]?.GetValue<string>() ?? "",
                hit["score"]?.GetValue<double>() ?? 0,
                (payload["findingIds"] as JsonArray)?.Select(id => Guid.Parse(id!.GetValue<string>())).ToList() ?? []);
        }).ToList();
    }
}
