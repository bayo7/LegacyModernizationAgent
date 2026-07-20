namespace LegacyModernizationAgent.Core.Abstractions;

/// <summary>
/// Analiz edilecek kaynağı hazırlar: git URL ise klonlar, zip ise açılmış klasörü
/// kullanır, lokal yol ise doğrular. Dönen değer analiz edilecek .sln/.csproj yolu
/// veya bunları içeren klasördür.
/// </summary>
public interface ISourcePreparer
{
    Task<string> PrepareAsync(string repoUrlOrPath, Guid jobId, CancellationToken ct = default);
}
