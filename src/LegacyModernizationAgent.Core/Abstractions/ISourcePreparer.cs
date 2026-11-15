namespace LegacyModernizationAgent.Core.Abstractions;

/// <summary>
/// Analiz edilecek kaynağı hazırlar: git URL ise klonlar, zip ise açılmış klasörü
/// kullanır, lokal yol ise doğrular. Dönen değer analiz edilecek .sln/.csproj yolu
/// veya bunları içeren klasördür.
/// </summary>
public interface ISourcePreparer
{
    Task<string> PrepareAsync(string repoUrlOrPath, Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Klonlanan/açılan kaynağın NuGet bağımlılıklarını geri yükler ('dotnet restore').
    /// Restore edilmemiş bir projede paket tipleri (xunit [Fact], IHealthCheck...) çözümlenemez
    /// ve semantik kurallar yanlış pozitif üretir. Başarısızlık analizi durdurmaz; loglanır.
    /// Kullanıcının verdiği lokal yollara dokunmaz.
    /// </summary>
    Task RestoreDependenciesAsync(string solutionOrProjectPath, CancellationToken ct = default);

    /// <summary>
    /// İş için oluşturulan geçici çalışma alanını (klon / açılmış zip / yüklenen zip) siler.
    /// Kullanıcının verdiği lokal yollara dokunmaz. Hata fırlatmaz; silinemeyenler loglanır.
    /// </summary>
    void Cleanup(Guid jobId);
}
