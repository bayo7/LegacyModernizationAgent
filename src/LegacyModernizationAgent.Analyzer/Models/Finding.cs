namespace LegacyModernizationAgent.Analyzer.Models;

/// <summary>
/// Bir analiz kuralının ürettiği tekil bulgu. Dil kuralı: bulgular asla "hata/bug"
/// olarak sunulmaz; her zaman "incelemeye değer" öneri dili kullanılır ve
/// <see cref="LimitationNote"/> kuralın sınırlamasını kullanıcıya açıklar.
/// </summary>
public record Finding(
    string RuleName,
    Severity Severity,
    Confidence Confidence,
    string FilePath,
    int LineNumber,
    string ClassName,
    string Description,
    string LimitationNote,
    Dictionary<string, object>? Metrics = null
);
