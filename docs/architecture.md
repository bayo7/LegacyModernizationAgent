# Mimari Kararlar ve Gerekçeleri

Bu doküman "neden böyle?" sorularının cevabıdır. Kod ne yaptığını gösterir; burada nedenler yazar.

## 1. Neden Analyzer bağımsız bir proje?

`LegacyModernizationAgent.Analyzer` yalnızca `Microsoft.CodeAnalysis*` paketlerine bağımlıdır — ASP.NET Core, EF Core veya başka üst katman referansı yoktur.

- **Test edilebilirlik:** Kurallar MSBuild olmadan, bellekte derlenen kod parçalarıyla test edilir (`CSharpCompilation` + `AdhocWorkspace`). 36 test ~0,6 saniyede koşar.
- **Yeniden kullanım:** Aynı motor hem API'den hem konsol Runner'dan tüketiliyor; ileride `dotnet legacy-scan` gibi bağımsız bir CLI aracına dönüştürme kapısı açık.

## 2. Kural tasarımı: iki arayüz

```
ICodeAnalyzer      → dosya (syntax tree) düzeyi: NPlusOne, GodClass, LongMethod
ISolutionAnalyzer  → solution düzeyi: CircularDependency, UnusedCode
```

Brief'teki tek arayüz yeterli değildi çünkü iki kural tek dosyaya bakarak çalışamaz: `UnusedCode` tüm solution'da referans arar (`SymbolFinder.FindReferencesAsync`), `CircularDependency` global bağımlılık grafiği ister. Her kural bağımsız sınıftır; yeni kural eklemek mevcut kodu değiştirmez (Open/Closed).

## 3. Yanlış pozitif stratejisi

Sistemin ana riski gürültü üretip güven kaybetmektir. Alınan önlemler:

- Her bulgu `Confidence` (High/Medium/Low) ve kullanıcıya gösterilen `LimitationNote` taşır.
- Rapor/UI dili asla "hata/bug" demez; "incelemeye değer" der.
- Auto-generated kod (Designer.cs, Migrations, obj/, `[GeneratedCode]`) analiz dışıdır.
- `UnusedCode` en riskli kuraldır; muafiyet listesi gerçek projede (eShopOnWeb) test edilerek genişletildi: controller action'ları, Razor PageModel handler'ları, Blazor/SignalR tipleri, middleware `Invoke(Async)`, interface implementasyonları, virtual/override, test yaşam döngüsü attribute'ları, record'ların derleyici üretimi metodları.
- `LongMethod` satır sayısını cyclomatic complexity ile birlikte değerlendirir: uzun ama düz mapping metodu Düşük önem alır, kısa ama iç içe koşullu metod da yakalanır.
- `CircularDependency` domain içi doğal döngüleri (Order ↔ OrderLine) Düşük, katmanlar arası döngüleri Yüksek önemle ayırır (namespace segmentlerinden katman tespiti).

## 4. Veri katmanı

- **SQLite varsayılan** (brief SQL Server DDL verir): kurulumsuz, dosya tabanlı — demo tek komutla ayağa kalkar. Şema Bölüm 5 DDL'iyle birebir eşleşir; `ConnectionStrings:Default` değiştirilerek SQL Server'a taşınabilir.
- **DDL'e bilinçli ekler:** `AnalysisJobs.ErrorMessage` (hata şeffaflığı) ve `ClassSummaries` tablosu — embedding'e giden özet metinler SQL'de de tutulur ki (1) Qdrant yokken chat'in anahtar kelime fallback'i çalışsın, (2) rapor bağlamı yeniden üretim olmadan kurulsun.
- Migration açılışta otomatik uygulanır (`Database.Migrate()`).

## 5. İş kuyruğu: IHostedService → Hangfire

Faz 2'de Channel tabanlı `IHostedService` yeterliydi; Faz 3'te embedding + LLM çağrıları analiz süresini dakikalara çıkarınca iki risk doğdu: uygulama yeniden başlarsa bellekteki kuyruk kaybolur ve API çağrılarının geçici hataları elle tekrar gerektirir. Hangfire ikisini de çözer (SQLite storage ile kalıcı kuyruk + otomatik retry) ve `/hangfire` dashboard'u demo değeri taşır.

Her iki implementasyon da `IAnalysisQueue` arkasında durur; `Jobs:UseHangfire=false` ile Channel sürümüne dönülebilir. Bilinçli sıra: önce basit olanı kur, ihtiyaç doğunca geçiş yap — aşırı mühendislik yok.

## 6. AI katmanı: tek istemci, iki sağlayıcı, her durumda çalışan sistem

- **OpenAI-uyumlu tek HTTP istemcisi:** Gemini de OpenAI-uyumlu endpoint sunduğu için (`.../v1beta/openai`) ayrı istemci yazılmadı; sağlayıcı seçimi yalnızca konfigürasyon (BaseUrl/model/VectorSize). Brief'in "hangi anahtar varsa onunla başla" açık sorusu böyle çözüldü.
- **Graceful degradation:** `Ai:ApiKey` boşsa rapor kural tabanlı deterministik fallback ile üretilir (Severity×Confidence önceliklendirmesi + kural başına önerilen aksiyon), chat SQL'deki özet metinlerde anahtar kelime aramasına düşer. Qdrant kapalıysa indeksleme loglanıp atlanır. Hiçbir eksik konfigürasyon pipeline'ı düşürmez; rapor `generator` alanıyla nasıl üretildiğini beyan eder.
- **İki LLM görevi ayrıdır** (brief 4.3): batch rapor üretimi (`ReportGenerationService`) ve RAG chat (`ChatService`). Chat: soru → embed → Qdrant top-5 (filtre: `analysisJobId`) → özetler + bulgular bağlam → cevap + kaynak dosya listesi.

## 7. Qdrant istemcisi neden elle yazıldı?

Resmî `Qdrant.Client` gRPC tabanlıdır ve ek bağımlılık getirir; ihtiyacımız dört REST ucu (koleksiyon kontrol/oluşturma, upsert, filtreli arama). `HttpClient` ile ~100 satırda, `IVectorStore` arayüzü arkasında yazıldı — ileride resmî istemciye geçiş tek sınıf değişikliğidir.

## 8. MSBuildWorkspace tuzakları (yaşanmış)

- `MSBuildLocator.RegisterDefaults()` **MSBuildWorkspace'ten önce** çağrılmalı; API'de `Program.cs`'in ilk satırıdır.
- `Microsoft.CodeAnalysis.CSharp.Workspaces` paketi olmadan `GetCompilationAsync` **sessizce null** döner (dil servisi yüklenmez) — bulunması zor bir hata, testle yakalandı.
- Locator, `Microsoft.Build.*` assembly'lerinin çıktıya kopyalanmasını yasaklar (MSBL001); kökteki `Directory.Build.props` bu paketleri `ExcludeAssets="runtime"` ile pin'ler.
- EF migration'ları için `IDesignTimeDbContextFactory` var — design-time'da API host'u (ve MSBuildLocator) ayağa kalkmaz.

## 9. Dashboard

Blazor Server (`LegacyModernizationAgent.Web`) API'yi **HTTP üzerinden** tüketir; sunucu katmanlarına proje referansı yoktur — mimarideki "Dashboard → API" okuna sadık, dashboard yarın ayrı makineye taşınabilir. Bağımlılık grafiği JS kütüphanesi olmadan sunucuda hesaplanan dairesel SVG yerleşimidir; döngü üyeleri renk + kesikli halka + lejant etiketiyle işaretlenir (renk tek başına anlam taşımaz), en bağlantılı 36 sınıf gösterilir.

## 10. Doğrulama geçmişi

- **Faz 1:** 36 birim test + eShopOnWeb (10 proje, 231 sınıf, ~12 sn). İlk koşuda 33 bulgunun ~20'si yanlış pozitifti (PageModel handler'ları, record metodları); muafiyetler eklendikten sonra kalan 12-13 bulgu elle incelendi ve gerçekçi bulundu (ör. `Basket.SetNewBuyerId` gerçekten ölü kod).
- **Faz 2/3:** API üzerinden uçtan uca: POST → Hangfire → Analyzing → Completed (~21 sn) → findings/report/dependencies/chat uçları doğrulandı (LLM'siz fallback modunda).
- **Faz 4:** Dashboard ana sayfa + detay sayfası canlı API'ye karşı render doğrulaması.
