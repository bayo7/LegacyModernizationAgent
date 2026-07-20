namespace LegacyModernizationAgent.Analyzer;

/// <summary>
/// Kural eşik değerleri. Başlangıç değerleri CLAUDE.md Bölüm 4.1'den; ileride
/// config dosyasına taşınabilir diye tek yerde toplandı.
/// </summary>
public class AnalyzerThresholds
{
    // God Class
    public int GodClassPublicMethodCount { get; init; } = 15;
    public int GodClassLineCount { get; init; } = 400;

    // Controller'larda çok sayıda endpoint doğaldır; eşik yükseltilir (varsayım:
    // standart eşiğin ~%60 üzeri — CLAUDE.md "eşik ayrıca yükseltilebilir" der,
    // kesin değer vermez).
    public int GodClassControllerPublicMethodCount { get; init; } = 25;
    public int GodClassControllerLineCount { get; init; } = 600;

    // Uzun Metod
    public int LongMethodLineCount { get; init; } = 50;

    /// <summary>Satır sayısından bağımsız olarak tek başına bulgu üreten complexity eşiği (varsayım).</summary>
    public int HighComplexityThreshold { get; init; } = 15;

    /// <summary>Uzun ama bu değerin altında complexity'ye sahip metodlar düşük severity alır (varsayım).</summary>
    public int LowComplexityThreshold { get; init; } = 8;

    public static AnalyzerThresholds Default { get; } = new();
}
