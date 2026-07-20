# Legacy Kod Modernizasyon Ajanı — Proje Brief'i

Bu doküman, Claude Code'un bu projeyi baştan sona geliştirirken referans alacağı ana kaynaktır. Her karar burada gerekçesiyle yazılı — amaç hiçbir detayın atlanmaması.

---

## 1. Proje Nedir, Neden Var

**Tek cümlelik konumlandırma:** SonarQube ve benzeri araçlar kod tabanını *tarar* ve kural ihlallerini listeler; bu sistem kod tabanını *anlar* ve gerekçeli, önceliklendirilmiş bir modernizasyon planı üretir.

**Problem:** Legacy .NET kod tabanlarında (özellikle kurumsal/endüstriyel yazılım firmalarında — ör. fabrika ERP sistemleri) geliştiriciler nereden başlayacağını bilemiyor. Statik analiz araçları yüzlerce uyarı üretiyor ama hangisinin gerçekten kritik olduğunu, hangisinin iş etkisinin büyük olduğunu söylemiyor.

**Çözüm:** Üç katmanlı bir pipeline:
1. **Roslyn** ile yapısal gerçekleri çıkar (ne nereye bağlı, hangi pattern'ler kötü kokuyor)
2. **Embeddings** ile kod tabanının anlamsal haritasını çıkar (hangi kod parçası hangi kavramla ilgili)
3. **LLM** ile bu ikisini birleştirip gerekçeli, önceliklendirilmiş bir rapor üret + kullanıcının doğal dilde soru sormasına izin ver (RAG chat)

**Geliştirici profili (context için):** 4. sınıf Yazılım Mühendisliği öğrencisi, C#/.NET ana stack, ASP.NET Core + EF Core + N-Tier mimaride deneyimli, önceki projelerinde RAG/embeddings (Gemini API, Qdrant/vector search) kullanmış (MyHealthAI projesi). Bu proje bir staj başvuru portföyü parçası — kod kalitesi ve mimari netlik, hız kadar önemli.

---

## 2. Genel Mimari

```
Kullanıcı (solution/repo yükler)
        │
        ▼
ASP.NET Core Web API (orkestrasyon, job yönetimi, chat endpoint)
        │
        ▼
KATMAN 1: Roslyn Statik Analiz
  - MSBuildWorkspace ile solution parse
  - Pattern analyzer'ları çalışır (N+1, God Class, Uzun Metod, Circular Dependency, Kullanılmayan Kod)
  - Bağımlılık grafiği çıkarılır
  → Çıktı: Findings (JSON/DB)
        │
        ▼
KATMAN 2: Semantik İndeksleme (Embeddings)
  - Her class/method için özet metin üretilir
  - Embedding modeliyle vektöre çevrilir
  - Vektör veritabanına yazılır
  → Çıktı: Aranabilir semantik indeks
        │
        ▼
KATMAN 3: LLM Muhakeme (RAG)
  - Roslyn bulguları + embedding bağlamı LLM'e verilir
  - Önceliklendirilmiş, gerekçeli rapor üretilir
  - Kullanıcı chat ile kod tabanına soru sorabilir
        │
        ▼
Dashboard (Blazor Server)
  - Risk skorlu dosya listesi
  - Bağımlılık grafiği görselleştirme
  - Chat arayüzü
```

---

## 3. Çözüm (Solution) Yapısı

```
LegacyModernizationAgent.sln
│
├── src/
│   ├── LegacyModernizationAgent.Api/          → ASP.NET Core Web API (giriş noktası)
│   ├── LegacyModernizationAgent.Core/          → Domain modelleri, interface'ler (N-Tier: Core katmanı)
│   ├── LegacyModernizationAgent.Application/   → Use case'ler, servisler, DTO'lar
│   ├── LegacyModernizationAgent.Infrastructure/→ EF Core, SQL, Qdrant, LLM client implementasyonları
│   ├── LegacyModernizationAgent.Analyzer/      → Roslyn analiz motoru (BAĞIMSIZ proje, sadece Microsoft.CodeAnalysis'e bağımlı)
│   └── LegacyModernizationAgent.Web/           → Blazor Server dashboard
│
├── tests/
│   ├── LegacyModernizationAgent.Analyzer.Tests/ → Roslyn analyzer'ları için unit test (KRİTİK — yanlış pozitif üretmemeli)
│   └── LegacyModernizationAgent.Application.Tests/
│
└── docs/
    └── architecture.md
```

**Neden Analyzer ayrı proje:** Roslyn analiz mantığının hiçbir ASP.NET Core bağımlılığı olmamalı — hem test edilebilirliği artırır hem de ileride bunu bağımsız bir CLI aracına (ör. `dotnet legacy-scan`) dönüştürme kapısını açık tutar. Bu ayrım mülakatta da "neden böyle ayırdın" sorusuna iyi bir cevap.

---

## 4. Katman Detayları

### 4.1 Roslyn Analiz Katmanı (`Analyzer` projesi)

**Kullanılacak paketler:** `Microsoft.CodeAnalysis.CSharp`, `Microsoft.CodeAnalysis.Workspaces.MSBuild`, `Microsoft.Build.Locator` (workspace'i doğru şekilde ayağa kaldırmak için gerekli — MSBuildWorkspace kullanmadan önce `MSBuildLocator.RegisterDefaults()` çağrılmalı, sık unutulan bir adım).

**Analyzer arayüz tasarımı (her kural bunu implement eder):**
```csharp
public interface ICodeAnalyzer
{
    string RuleName { get; }
    Severity DefaultSeverity { get; }
    IEnumerable<Finding> Analyze(SyntaxNode root, SemanticModel semanticModel, string filePath);
}
```
Bu tasarım önemli: her kural bağımsız, test edilebilir, yeni kural eklemek mevcut kodu bozmuyor (Open/Closed Principle — mülakatta vurgulanacak bir nokta).

**Faz 1 kuralları (öncelik sırası ve teknik detay):**

> **Genel prensip — ÖNEMLİ:** Hiçbir statik metrik (satır sayısı, metod sayısı vb.) tek başına "kötü kod" kanıtı değildir, sadece bir **sezgisel sinyaldir.** Her kural aşağıda hem "neyi ölçüyor" hem de "nerede yanılabilir" ile birlikte tanımlanmıştır. Bu sınırlamalar `Finding.Confidence` alanına ve rapor metnine yansıtılmalı — sistem asla "bu kesin hatalı" değil, "bu incelemeye değer, çünkü..." dili kullanmalı.

1. **N+1 Query Detector**
   - Ara: `ForEachStatementSyntax` düğümleri
   - Döngü gövdesinde: döngü değişkeninin bir navigation property'sine `MemberAccessExpressionSyntax` ile erişilip hemen ardından `.ToList()`, `.Count()`, `.Any()`, `.First()` gibi bir `InvocationExpressionSyntax` çağrılıyor mu kontrol et
   - `SemanticModel` kullanarak bu property'nin gerçekten bir EF Core navigation property olup olmadığını doğrula (sadece isim eşleşmesi değil — yanlış pozitifi azaltır)
   - Daha önce aynı sorguda `.Include()` çağrılmış mı kontrol et (varsa bulgu üretme)
   - **Nerede yanılabilir:** Koleksiyon zaten bellekte yüklüyse (ör. `AsNoTracking()` sonrası materialize edilmiş bir liste üzerinde döngü) veya lazy loading kasıtlı olarak kapatılmışsa bu bir sorun değildir. Bu yüzden `Confidence: Orta` ile işaretlenmeli — kesin değil, "kontrol et" seviyesinde.

2. **God Class Detector**
   - `ClassDeclarationSyntax` başına: public metod sayısı + toplam satır sayısı hesapla
   - Eşik (başlangıç için, config'e taşınabilir): 15+ public metod VEYA 400+ satır
   - **Nerede yanılabilir:** DTO/Model class'ları, Controller'lar (çok sayıda endpoint barındırması doğal) ve auto-generated kod (Designer.cs, Migrations) yanlış pozitif üretir. **Zorunlu istisna listesi:** dosya adı `*.Designer.cs`, `*Migrations/*`, veya class `[GeneratedCode]` attribute'üne sahipse analiz dışı bırakılmalı. Controller'lar için eşik ayrıca yükseltilebilir (endpoint sayısı doğal olarak metod sayısını artırır).

3. **Uzun Metod Detector**
   - `MethodDeclarationSyntax` başına satır sayısı say
   - Eşik: 50+ satır
   - **Nerede yanılabilir (senin sorduğun nokta):** Satır sayısı karmaşıklığın zayıf bir vekilidir. Bu yüzden satır sayısına ek olarak **Cyclomatic Complexity** hesapla (if/else/for/while/case/&&/|| sayısı + 1). Bir metod satır olarak uzun ama complexity düşükse (ör. düz bir switch-case mapping'i), severity düşür; satır kısa ama complexity yüksekse (iç içe koşullar), severity yükselt. İki metriği birlikte raporla: `LineCount` ve `CyclomaticComplexity` her ikisi de `Finding`'de tutulmalı, karar ikisine birden bakılarak verilmeli.

4. **Circular Dependency Detector**
   - Önce class → class bağımlılık grafiğini çıkar (her class'ın field/property/method parametrelerinde referans ettiği diğer class'lar)
   - Basit bir cycle detection algoritması (DFS ile) çalıştır
   - **Nerede yanılabilir:** Bazı circular referanslar kasıtlı ve zararsızdır (ör. parent-child ilişkisi, `Order` ↔ `OrderLine` gibi domain modelinde doğal karşılıklı referans). Sadece **farklı katmanlar arası** (ör. `Infrastructure` bir `Application` sınıfına referans veriyor ve tersi de doğru) circular dependency'ler yüksek severity almalı; aynı katman/domain içindeki karşılıklı referanslar düşük severity ile raporlanmalı.

5. **Kullanılmayan Kod Detector**
   - `SymbolFinder.FindReferencesAsync` (Roslyn'in workspace-level API'si) ile her public metodun referans sayısını bul
   - 0 referanslı ve `internal`/`public` olan metodları işaretle
   - **Nerede yanılabilir (en riskli kural bu):** Interface implementasyonları (ör. bir controller action'ı hiçbir yerden C# kodu içinden çağrılmaz, routing ile çağrılır), reflection ile çağrılanlar, dependency injection ile resolve edilenler, ve public API yüzeyi (bir kütüphanenin dışa açık metodları) false positive üretir. **Zorunlu filtre:** `[ApiController]` içindeki action metodları, interface implementasyonları (`.Implements()` kontrolü) ve `virtual`/`override` metodlar bu kuraldan muaf tutulmalı. Bu kural `Confidence: Düşük` ile başlamalı, raporda en dikkatli sunulması gereken kural bu.

**Çıktı modeli:**
```csharp
public record Finding(
    string RuleName,
    Severity Severity,
    Confidence Confidence,      // Yüksek / Orta / Düşük — kuralın ne kadar güvenilir olduğu
    string FilePath,
    int LineNumber,
    string ClassName,
    string Description,
    string LimitationNote,      // "Bu bulgu satır sayısına dayalı bir sezgisel göstergedir, otomatik doğruluk garantisi taşımaz" gibi
    Dictionary<string, object>? Metrics = null // ör. { "LineCount": 62, "CyclomaticComplexity": 4 }
);
```

**Rapor/UI dili kuralı:** Sistem hiçbir bulguyu "hata" ya da "bug" olarak sunmamalı. Her zaman "incelemeye değer", "kontrol edilmesi önerilir" gibi öneri dili kullanılmalı — bu hem teknik olarak daha doğru hem de kullanıcı güvenini artırır.

### 4.2 Embedding / Semantik İndeksleme Katmanı

**Amaç (netleştirdiğimiz gibi):** Roslyn "ne nereye bağlı" bilgisini veriyor; embedding "hangi kod parçası hangi kavramla ilgili" bilgisini veriyor.

**İndekslenecek birim:** Class seviyesi (method seviyesi Faz 2'ye bırakılabilir, ilk versiyonda gürültüyü azaltır).

**Özet metin şablonu (embedding'e gönderilmeden önce):**
```
Class: {ClassName}
Namespace: {Namespace}
Public Metodlar: {MethodSignatures}
XML Doc (varsa): {XmlDocSummary}
İlişkili Bulgular: {FindingsForThisClass}
```

**Embedding modeli:** OpenAI `text-embedding-3-small` (maliyet/performans dengesi iyi, önceki projelerinde Gemini `text-embedding-004` de kullanmıştın — ikisi de uygun, API anahtarı hangisi varsa onunla başla).

**Vektör veritabanı:** Qdrant, Docker ile lokal (`docker run -p 6333:6333 qdrant/qdrant`). Koleksiyon adı: `code_embeddings`. Her point'in payload'ında: `analysisJobId`, `filePath`, `className`, `findingIds`.

### 4.3 LLM Muhakeme Katmanı

**İki ayrı LLM görevi var, ikisini karıştırma:**

**a) Rapor üretimi (batch, analiz bittiğinde bir kez çalışır):**
- Input: Tüm `Findings` listesi (gruplu) + her kritik class için embedding'den çekilen "semantik komşular"
- Prompt yapısı: "Sen bir senior .NET mimarisin. Aşağıdaki statik analiz bulgularını ve kod tabanı bağlamını incele. Her bulguyu iş etkisi ve düzeltme zorluğuna göre önceliklendir (Yüksek/Orta/Düşük), gerekçesini yaz, önerilen çözümü 2-3 cümlede özetle. SADECE JSON formatında yanıt ver."
- Output: Yapılandırılmış JSON, `Reports` tablosuna yazılır

**b) Chat (RAG, kullanıcı her soru sorduğunda çalışır):**
- Input: Kullanıcı sorusu → embed et → Qdrant'ta en yakın 5 class'ı bul → o class'ların özet metinleri + ilgili Findings'i context olarak topla
- Prompt: "Aşağıdaki kod tabanı bağlamını kullanarak kullanıcının sorusunu yanıtla. Hangi dosya/class'lardan bahsettiğini belirt."
- Output: Serbest metin cevap + kaynak dosya listesi

---

## 5. Veri Modeli (SQL DDL Taslağı)

```sql
CREATE TABLE AnalysisJobs (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    RepoUrlOrPath NVARCHAR(500) NOT NULL,
    Status NVARCHAR(50) NOT NULL, -- Queued, Analyzing, Indexing, Reporting, Completed, Failed
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CompletedAt DATETIME2 NULL
);

CREATE TABLE Findings (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    AnalysisJobId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES AnalysisJobs(Id),
    RuleName NVARCHAR(100) NOT NULL,
    Severity NVARCHAR(20) NOT NULL,
    Confidence NVARCHAR(20) NOT NULL,       -- Yüksek / Orta / Düşük (bkz. Bölüm 4.1 — her kuralın yanılma riski)
    FilePath NVARCHAR(500) NOT NULL,
    LineNumber INT NOT NULL,
    ClassName NVARCHAR(200) NULL,
    Description NVARCHAR(MAX) NOT NULL,
    LimitationNote NVARCHAR(500) NULL,      -- kuralın sınırlamasına dair kullanıcıya gösterilecek not
    MetricsJson NVARCHAR(MAX) NULL          -- ör. { "LineCount": 62, "CyclomaticComplexity": 4 }
);

CREATE TABLE ClassDependencies (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    AnalysisJobId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES AnalysisJobs(Id),
    FromClass NVARCHAR(200) NOT NULL,
    ToClass NVARCHAR(200) NOT NULL
);

CREATE TABLE Reports (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    AnalysisJobId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES AnalysisJobs(Id),
    GeneratedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    ReportJson NVARCHAR(MAX) NOT NULL
);

CREATE TABLE ChatMessages (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    AnalysisJobId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES AnalysisJobs(Id),
    Role NVARCHAR(20) NOT NULL, -- user, assistant
    Content NVARCHAR(MAX) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);
```

*(Qdrant'taki vektör verisi SQL dışında tutulur, sadece `AnalysisJobId` ile ilişkilendirilir.)*

---

## 6. API Endpoint Taslağı

| Method | Route | Açıklama |
|---|---|---|
| POST | `/api/analysis` | Yeni analiz başlat (repo URL veya zip upload) |
| GET | `/api/analysis/{id}` | Job durumunu sorgula |
| GET | `/api/analysis/{id}/findings` | Ham bulgular listesi |
| GET | `/api/analysis/{id}/report` | Önceliklendirilmiş rapor |
| GET | `/api/analysis/{id}/dependencies` | Bağımlılık grafiği verisi |
| POST | `/api/analysis/{id}/chat` | RAG chat mesajı gönder |
| GET | `/api/analysis/{id}/chat` | Chat geçmişini getir |

---

## 7. Fazlı Geliştirme Planı

### Faz 1 — Çekirdek Analiz Motoru (2–3 hafta)
- [ ] Solution iskeletini kur (Bölüm 3'teki yapı)
- [ ] `Analyzer` projesinde `ICodeAnalyzer` arayüzü + 5 kuralı implement et
- [ ] Her kural için unit test (bilerek kötü kod örnekleri yazıp analyzer'ın yakaladığını doğrula)
- [ ] Basit bir console runner ile bir solution üzerinde çalıştırıp JSON çıktı al
- **Definition of Done:** Gerçek bir açık kaynak legacy .NET projesinde çalıştırıldığında anlamlı, düşük yanlış-pozitifli bulgular üretiyor

### Faz 2 — Veri Katmanı + LLM Rapor (2 hafta)
- [ ] EF Core ile DB şemasını kur (Bölüm 5)
- [ ] Web API'de upload → analiz tetikleme akışı
- [ ] LLM rapor üretim servisi (Bölüm 4.3-a)
- **Definition of Done:** "Yükle → Analiz Et → Önceliklendirilmiş Rapor Al" uçtan uca çalışıyor

### Faz 3 — Semantik Katman + Chat (2 hafta)
- [ ] Qdrant Docker kurulumu + embedding pipeline
- [ ] RAG chat endpoint'i (Bölüm 4.3-b)
- [ ] **Job kuyruğunu `IHostedService`'ten Hangfire'a geçir** (bkz. not aşağıda)
- **Definition of Done:** Doğal dil sorusuna doğru dosyaları kaynak göstererek cevap veriyor

**Neden Hangfire'a bu aşamada geçiliyor:** Faz 1-2'de `IHostedService` yeterli — kurulumu basit, ekstra bağımlılık yok, hızlı ilerlemeyi sağlıyor. Ama analiz süresi Faz 3'te (embedding + LLM çağrıları eklenince) dakikalara çıkıyor; bu noktada iki risk devreye giriyor: (1) uygulama yeniden başlarsa `IHostedService`'in bellekteki kuyruğu kaybolur, kullanıcı analizi yeniden başlatmak zorunda kalır, (2) LLM/embedding API çağrıları ara sıra başarısız olabilir, otomatik retry mekanizması olmadan kullanıcı elle tekrar denemek zorunda kalır. Hangfire bu ikisini de (kalıcı kuyruk + otomatik retry) çözüyor, üstelik hazır bir dashboard (`/hangfire`) ile job durumlarını görselleştiriyor — bu da demo sırasında gösterilebilecek ekstra bir özellik. Mülakatta anlatılabilecek hikaye: "Baştan aşırı mühendislik yapmadım, ihtiyaç ortaya çıkınca bilinçli bir geçiş yaptım."

### Faz 4 — Dashboard + Cilalama (1–2 hafta)
- [ ] Blazor Server dashboard (risk listesi, bağımlılık grafiği, chat arayüzü)
- [ ] Gerçek bir açık kaynak projede uçtan uca demo testi
- [ ] README + mimari diyagram + demo videosu

---

## 8. Claude Code İçin Çalışma Talimatları

- Faz 1'den başla, sırayla ilerle — fazları atlama.
- Her analyzer kuralı için önce başarısız olması gereken bir unit test yaz (TDD yaklaşımı), sonra implementasyonu yap.
- Roslyn API'lerinde emin olmadığın noktalarda (özellikle `SemanticModel` kullanımı, `MSBuildWorkspace` kurulumu) varsayımda bulunma, güncel dokümantasyonu kontrol et.
- Kod stilinde: N-Tier ayrımına sadık kal, `Analyzer` projesinin `Infrastructure`/`Api` projelerine bağımlı olmamasına dikkat et.
- Her faz sonunda "Definition of Done" kriterini gerçek bir örnek projeyle doğrula, sadece unit testlere güvenme.
- Belirsiz bir tasarım kararıyla karşılaşırsan (ör. eşik değerleri, prompt formatı) makul bir varsayımla devam et ama bunu commit mesajında veya yorumda not düş.

---

## 9. Açık Sorular / Sonraki Konuşmada Netleştirilecekler

- Embedding + LLM API sağlayıcısı: OpenAI mi Gemini mi kesin karar (API anahtarı erişimine göre)
- Faz 1 demo projesi olarak hangi açık kaynak .NET reposu kullanılacak (küçük-orta ölçekli, gerçekçi legacy pattern'ler içeren bir tane seçilmeli)

*(Not: Job kuyruğu kararı netleşti — Bölüm 7, Faz 3'e bakınız: `IHostedService` ile başla, Hangfire'a planlı geçiş yap.)*
