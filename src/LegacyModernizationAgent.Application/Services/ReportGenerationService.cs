using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LegacyModernizationAgent.Core.Abstractions;
using LegacyModernizationAgent.Core.Entities;
using Microsoft.Extensions.Logging;

namespace LegacyModernizationAgent.Application.Services;

public interface IReportGenerationService
{
    Task<Report> GenerateAsync(Guid jobId, CancellationToken ct = default);
}

/// <summary>
/// Rapor üretimi (CLAUDE.md 4.3-a): tüm bulgular + kritik sınıfların semantik
/// komşuları LLM'e verilir, önceliklendirilmiş JSON rapor alınır.
/// LLM yapılandırılmamışsa kural tabanlı deterministik bir fallback rapor üretilir —
/// API anahtarı olmadan da uçtan uca akış çalışır, rapor "generator" alanıyla işaretlenir.
/// </summary>
public class ReportGenerationService(
    IFindingRepository findings,
    IReportRepository reports,
    ILlmClient llm,
    ILogger<ReportGenerationService> logger) : IReportGenerationService
{
    private const string SystemPrompt =
        "Sen bir senior .NET mimarisin. Aşağıdaki statik analiz bulgularını ve kod tabanı bağlamını incele. " +
        "Her bulguyu iş etkisi ve düzeltme zorluğuna göre önceliklendir (Yüksek/Orta/Düşük), gerekçesini yaz, " +
        "önerilen çözümü 2-3 cümlede özetle. Bulgular kesin hata değil sezgisel sinyaldir; 'incelemeye değer' dili kullan, " +
        "her bulgunun Confidence değerini dikkate al. SADECE şu şemada JSON formatında yanıt ver: " +
        """{"summary":"genel değerlendirme","items":[{"findingId":"guid","priority":"Yüksek|Orta|Düşük","title":"kısa başlık","rationale":"gerekçe","suggestedAction":"önerilen çözüm"}]}""";

    public async Task<Report> GenerateAsync(Guid jobId, CancellationToken ct = default)
    {
        var jobFindings = await findings.GetByJobAsync(jobId, ct);
        var summaries = await findings.GetClassSummariesAsync(jobId, ct);

        JsonObject reportBody;

        if (llm.IsConfigured && jobFindings.Count > 0)
        {
            try
            {
                reportBody = await GenerateWithLlmAsync(jobFindings, summaries, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "LLM rapor üretimi başarısız, fallback rapora geçiliyor.");
                reportBody = GenerateFallback(jobFindings);
                reportBody["generatorNote"] = $"LLM çağrısı başarısız olduğu için kural tabanlı rapor üretildi: {ex.Message}";
            }
        }
        else
        {
            reportBody = GenerateFallback(jobFindings);
        }

        reportBody["analysisJobId"] = jobId.ToString();
        reportBody["generatedAtUtc"] = DateTime.UtcNow.ToString("O");
        reportBody["findingCount"] = jobFindings.Count;

        var report = new Report
        {
            AnalysisJobId = jobId,
            ReportJson = reportBody.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
        };
        await reports.SaveAsync(report, ct);
        return report;
    }

    private async Task<JsonObject> GenerateWithLlmAsync(
        IReadOnlyList<Finding> jobFindings,
        IReadOnlyList<ClassSummary> summaries,
        CancellationToken ct)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("## Statik Analiz Bulguları (kurala göre gruplu)");

        foreach (var group in jobFindings.GroupBy(f => f.RuleName))
        {
            prompt.AppendLine($"### Kural: {group.Key} ({group.Count()} bulgu)");
            foreach (var f in group.Take(30)) // token bütçesi: kural başına 30 bulgu yeterli temsil sağlar
            {
                prompt.AppendLine(
                    $"- findingId: {f.Id} | {f.Severity}/{f.Confidence} | {f.ClassName} ({Path.GetFileName(f.FilePath)}:{f.LineNumber}) | {f.Description}");
            }
        }

        // Bulgusu olan sınıfların özetleri "semantik bağlam" olarak eklenir.
        var criticalClasses = jobFindings
            .Where(f => !string.IsNullOrEmpty(f.ClassName))
            .Select(f => f.ClassName!)
            .Distinct()
            .Take(20)
            .ToHashSet();

        var relevantSummaries = summaries.Where(s => criticalClasses.Contains(s.ClassName)).Take(20).ToList();
        if (relevantSummaries.Count > 0)
        {
            prompt.AppendLine("\n## Kod Tabanı Bağlamı (bulgu içeren sınıfların özetleri)");
            foreach (var s in relevantSummaries)
                prompt.AppendLine($"---\n{s.SummaryText}");
        }

        var response = await llm.CompleteAsync(SystemPrompt, prompt.ToString(), ct);
        var json = ExtractJson(response);

        var parsed = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("LLM yanıtı JSON object olarak parse edilemedi.");
        parsed["generator"] = "llm";
        return parsed;
    }

    /// <summary>LLM'ler bazen JSON'u ```json ... ``` bloğuna sarar; ayıkla.</summary>
    internal static string ExtractJson(string response)
    {
        var text = response.Trim();
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
                text = text[(firstNewline + 1)..lastFence].Trim();
        }

        // Yanıtın başında/sonunda açıklama metni varsa ilk { ile son } arasını al.
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    /// <summary>
    /// Kural tabanlı önceliklendirme: Severity ve Confidence birlikte değerlendirilir
    /// (yüksek severity + düşük confidence olan bulgu, orta önceliğe iner).
    /// </summary>
    private static JsonObject GenerateFallback(IReadOnlyList<Finding> jobFindings)
    {
        var items = new JsonArray();

        foreach (var f in jobFindings.OrderByDescending(x => PriorityScore(x)))
        {
            items.Add(new JsonObject
            {
                ["findingId"] = f.Id.ToString(),
                ["priority"] = PriorityLabel(f),
                ["title"] = $"{f.RuleName}: {f.ClassName ?? Path.GetFileName(f.FilePath)}",
                ["rationale"] = f.Description + (string.IsNullOrEmpty(f.LimitationNote) ? "" : $" (Not: {f.LimitationNote})"),
                ["suggestedAction"] = SuggestedAction(f.RuleName)
            });
        }

        return new JsonObject
        {
            ["generator"] = "offline-fallback",
            ["summary"] =
                jobFindings.Count == 0
                    ? "Analizde incelemeye değer bulgu tespit edilmedi."
                    : $"Toplam {jobFindings.Count} bulgu tespit edildi. Bu rapor LLM olmadan kural tabanlı üretildi; " +
                      "öncelikler Severity ve Confidence değerlerinin birleşiminden hesaplandı. " +
                      "LLM yapılandırıldığında iş etkisi analizi içeren gerekçeli rapor üretilir.",
            ["items"] = items
        };
    }

    private static int PriorityScore(Finding f)
    {
        var severity = f.Severity switch { "High" => 3, "Medium" => 2, _ => 1 };
        var confidence = f.Confidence switch { "High" => 3, "Medium" => 2, _ => 1 };
        return severity * 10 + confidence;
    }

    private static string PriorityLabel(Finding f) => (f.Severity, f.Confidence) switch
    {
        ("High", "High" or "Medium") => "Yüksek",
        ("High", _) => "Orta",
        ("Medium", _) => "Orta",
        _ => "Düşük"
    };

    private static string SuggestedAction(string ruleName) => ruleName switch
    {
        "NPlusOneQuery" =>
            "Sorguyu .Include()/.ThenInclude() ile tek seferde yükleyin veya ihtiyaç duyulan alanları Select ile projekte edin. " +
            "Koleksiyonun zaten bellekte olup olmadığını kontrol edin.",
        "GodClass" =>
            "Sınıfın sorumluluklarını listeleyin ve tek sorumluluk ilkesine göre daha küçük sınıflara ayırın. " +
            "Önce en az bağımlı metod grubunu çıkarmak güvenli bir başlangıçtır.",
        "LongMethod" =>
            "Metodu anlamlı isimli küçük metodlara bölün; iç içe koşulları erken dönüş (guard clause) ile sadeleştirin. " +
            "Cyclomatic complexity düşükse yalnızca okunabilirlik için bölmek yeterlidir.",
        "CircularDependency" =>
            "Bağımlılık yönünü tek yöne çevirin: ortak davranışı bir arayüze çıkarıp bağımlılığı ters çevirmek (DIP) " +
            "veya paylaşılan tipleri ayrı bir modüle taşımak tipik çözümlerdir.",
        "UnusedCode" =>
            "Metodun reflection, DI veya harici tüketiciler tarafından kullanılmadığını doğruladıktan sonra silin. " +
            "Emin değilseniz önce [Obsolete] ile işaretleyip bir sürüm bekleyin.",
        _ => "Bulguyu bağlamıyla birlikte gözden geçirin."
    };
}
