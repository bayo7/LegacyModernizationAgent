namespace LegacyModernizationAgent.Core.Entities;

/// <summary>
/// DDL'e bilinçli ek: embedding'e gönderilen class özet metinleri SQL'de de tutulur.
/// Gerekçe — (1) Qdrant erişilemezken chat'in anahtar kelime tabanlı fallback'i
/// çalışabilsin, (2) özet metinler yeniden üretilmeden rapor bağlamına eklenebilsin.
/// </summary>
public class ClassSummary
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnalysisJobId { get; set; }
    public string ClassName { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string SummaryText { get; set; } = "";
}
