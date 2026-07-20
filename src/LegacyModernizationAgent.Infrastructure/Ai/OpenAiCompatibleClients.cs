using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using LegacyModernizationAgent.Core.Abstractions;
using Microsoft.Extensions.Options;

namespace LegacyModernizationAgent.Infrastructure.Ai;

public class OpenAiCompatibleLlmClient(HttpClient http, IOptions<AiOptions> options) : ILlmClient
{
    private readonly AiOptions _options = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("LLM istemcisi yapılandırılmamış (Ai:ApiKey boş).");

        var request = new
        {
            model = _options.ChatModel,
            temperature = 0.2, // rapor üretiminde tutarlılık > yaratıcılık
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Authorization = new("Bearer", _options.ApiKey);

        var response = await http.SendAsync(message, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"LLM API {(int)response.StatusCode}: {Truncate(body)}");

        var content = JsonNode.Parse(body)?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
        return content ?? throw new InvalidOperationException("LLM yanıtında content alanı yok.");
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500];
}

public class OpenAiCompatibleEmbeddingClient(HttpClient http, IOptions<AiOptions> options) : IEmbeddingClient
{
    private readonly AiOptions _options = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);
    public int VectorSize => _options.EmbeddingVectorSize;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Embedding istemcisi yapılandırılmamış (Ai:ApiKey boş).");

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/embeddings")
        {
            Content = JsonContent.Create(new { model = _options.EmbeddingModel, input = texts })
        };
        message.Headers.Authorization = new("Bearer", _options.ApiKey);

        var response = await http.SendAsync(message, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Embedding API {(int)response.StatusCode}: {body[..Math.Min(body.Length, 500)]}");

        var data = JsonNode.Parse(body)?["data"] as JsonArray
            ?? throw new InvalidOperationException("Embedding yanıtında data alanı yok.");

        // API sırayı korur ama index alanına güvenmek daha sağlam.
        return data
            .OrderBy(item => item!["index"]!.GetValue<int>())
            .Select(item => item!["embedding"]!.AsArray().Select(v => v!.GetValue<float>()).ToArray())
            .ToList();
    }
}
