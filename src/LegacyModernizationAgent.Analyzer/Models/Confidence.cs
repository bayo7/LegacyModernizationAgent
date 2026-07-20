namespace LegacyModernizationAgent.Analyzer.Models;

/// <summary>
/// Kuralın ne kadar güvenilir olduğu — statik analiz sezgiseldir, her kuralın
/// yanlış pozitif üretme riski farklıdır. Raporda bu alan kullanıcıya açıkça gösterilir.
/// </summary>
public enum Confidence
{
    Low,
    Medium,
    High
}
