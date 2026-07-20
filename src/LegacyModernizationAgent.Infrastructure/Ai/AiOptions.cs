namespace LegacyModernizationAgent.Infrastructure.Ai;

/// <summary>
/// LLM/Embedding sağlayıcı ayarları. OpenAI-uyumlu API varsayılır; Gemini de
/// OpenAI-uyumlu endpoint'i üzerinden aynı istemciyle kullanılabilir:
///   BaseUrl: https://generativelanguage.googleapis.com/v1beta/openai
///   ChatModel: gemini-2.0-flash, EmbeddingModel: text-embedding-004 (VectorSize: 768)
/// Böylece tek istemci implementasyonu iki sağlayıcıyı da kapsar (bilinçli tasarım kararı —
/// CLAUDE.md Bölüm 9'daki açık soruyu "hangi anahtar varsa o" esnekliğiyle çözer).
/// </summary>
public class AiOptions
{
    public const string SectionName = "Ai";

    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ChatModel { get; set; } = "gpt-4o-mini";
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public int EmbeddingVectorSize { get; set; } = 1536;
}
