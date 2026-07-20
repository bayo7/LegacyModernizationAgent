using System.Text;
using LegacyModernizationAgent.Core.Abstractions;
using LegacyModernizationAgent.Core.Entities;
using Microsoft.Extensions.Logging;

namespace LegacyModernizationAgent.Application.Services;

public record ChatAnswer(string Content, IReadOnlyList<string> SourceFiles);

public interface IChatService
{
    Task<ChatAnswer> AskAsync(Guid jobId, string question, CancellationToken ct = default);
}

/// <summary>
/// RAG chat (CLAUDE.md 4.3-b): soru embed edilir → Qdrant'tan en yakın 5 sınıf →
/// özetleri + bulguları bağlam olarak LLM'e verilir. Embedding/Qdrant yoksa SQL'deki
/// özet metinler üzerinde anahtar kelime araması fallback'i kullanılır; LLM de yoksa
/// bulunan bağlam şablon cevapla döndürülür — sistem hiçbir konfigürasyonda sessizce ölmez.
/// </summary>
public class ChatService(
    IChatRepository chat,
    IFindingRepository findings,
    IEmbeddingClient embeddings,
    IVectorStore vectorStore,
    ILlmClient llm,
    ILogger<ChatService> logger) : IChatService
{
    private const int TopK = 5;

    public async Task<ChatAnswer> AskAsync(Guid jobId, string question, CancellationToken ct = default)
    {
        await chat.AddAsync(new ChatMessage { AnalysisJobId = jobId, Role = "user", Content = question }, ct);

        var context = await RetrieveContextAsync(jobId, question, ct);
        var answer = await ComposeAnswerAsync(jobId, question, context, ct);

        await chat.AddAsync(new ChatMessage { AnalysisJobId = jobId, Role = "assistant", Content = answer.Content }, ct);
        return answer;
    }

    private sealed record ContextItem(string ClassName, string FilePath, string SummaryText);

    private async Task<List<ContextItem>> RetrieveContextAsync(Guid jobId, string question, CancellationToken ct)
    {
        // Birincil yol: vektör araması
        if (embeddings.IsConfigured && await vectorStore.IsAvailableAsync(ct))
        {
            try
            {
                var queryVector = (await embeddings.EmbedAsync([question], ct))[0];
                var hits = await vectorStore.SearchAsync(jobId, queryVector, TopK, ct);
                if (hits.Count > 0)
                    return hits.Select(h => new ContextItem(h.ClassName, h.FilePath, h.SummaryText)).ToList();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Vektör araması başarısız; anahtar kelime fallback'ine geçiliyor.");
            }
        }

        // Fallback: SQL'deki özet metinlerde anahtar kelime skoru
        var summaries = await findings.GetClassSummariesAsync(jobId, ct);
        var tokens = question
            .Split([' ', ',', '.', '?', '!', ';', ':', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 2)
            .Select(t => t.ToLowerInvariant())
            .Distinct()
            .ToList();

        return summaries
            .Select(s => new
            {
                Summary = s,
                Score = tokens.Count(t =>
                    s.SummaryText.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    s.ClassName.Contains(t, StringComparison.OrdinalIgnoreCase))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(TopK)
            .Select(x => new ContextItem(x.Summary.ClassName, x.Summary.FilePath, x.Summary.SummaryText))
            .ToList();
    }

    private async Task<ChatAnswer> ComposeAnswerAsync(Guid jobId, string question, List<ContextItem> context, CancellationToken ct)
    {
        var sourceFiles = context.Select(c => c.FilePath).Distinct().ToList();

        if (!llm.IsConfigured)
        {
            var sb = new StringBuilder();
            sb.AppendLine("LLM yapılandırılmadığı için otomatik derlenen bağlam sunuluyor (Ai:ApiKey ayarlandığında doğal dil cevap üretilir).");
            if (context.Count == 0)
            {
                sb.AppendLine("Sorunuzla eşleşen sınıf bulunamadı. Daha spesifik bir sınıf/kavram adıyla tekrar deneyin.");
            }
            else
            {
                sb.AppendLine($"Sorunuzla en ilgili görünen {context.Count} sınıf:");
                foreach (var item in context)
                    sb.AppendLine($"\n--- {item.ClassName} ({item.FilePath})\n{item.SummaryText}");
            }

            return new ChatAnswer(sb.ToString().TrimEnd(), sourceFiles);
        }

        var contextText = new StringBuilder();
        foreach (var item in context)
            contextText.AppendLine($"--- Dosya: {item.FilePath}\n{item.SummaryText}\n");

        var history = await chat.GetByJobAsync(jobId, ct);
        var recentHistory = history.TakeLast(11).SkipLast(1).ToList(); // son kullanıcı mesajı zaten soruda
        var historyText = string.Join("\n", recentHistory.Select(m => $"{m.Role}: {m.Content}"));

        var systemPrompt =
            "Sen bir .NET kod tabanı analiz asistanısın. Aşağıdaki kod tabanı bağlamını kullanarak kullanıcının sorusunu yanıtla. " +
            "Hangi dosya/class'lardan bahsettiğini açıkça belirt. Bağlamda cevap yoksa bunu dürüstçe söyle; uydurma. " +
            "Bulgular sezgisel sinyaldir — 'kesin hata' değil 'incelemeye değer' dili kullan.";

        var userPrompt =
            $"## Kod Tabanı Bağlamı\n{contextText}\n" +
            (string.IsNullOrWhiteSpace(historyText) ? "" : $"## Önceki Konuşma\n{historyText}\n") +
            $"## Soru\n{question}";

        var content = await llm.CompleteAsync(systemPrompt, userPrompt, ct);
        return new ChatAnswer(content, sourceFiles);
    }
}
