namespace LegacyModernizationAgent.Application.Services;

/// <summary>
/// Bulgu ↔ sınıf özeti eşleştirme anahtarı. Yalnızca kısa sınıf adı kullanılırsa farklı
/// namespace'lerdeki aynı adlı sınıfların (ör. iki ayrı 'Helper') bulguları birbirine karışır;
/// her bulgu sınıfın tanımlandığı dosya yolunu da taşıdığı için anahtar dosya + ad çiftidir.
/// </summary>
internal static class ClassKey
{
    public static string Of(string className, string filePath) => $"{filePath}::{className}";
}
