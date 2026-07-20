using System.Text.Json;
using System.Text.Json.Serialization;
using LegacyModernizationAgent.Analyzer.Engine;
using LegacyModernizationAgent.Analyzer.Models;

// Faz 1 console runner: bir solution/proje yolunu analiz eder, JSON çıktı üretir.
// Kullanım: dotnet run --project src/LegacyModernizationAgent.Runner -- <sln-veya-csproj-yolu> [cikti.json]

if (args.Length == 0)
{
    Console.Error.WriteLine("Kullanım: LegacyModernizationAgent.Runner <solution.sln | proje.csproj | klasör> [çıktı.json]");
    return 1;
}

var inputPath = args[0];
var outputPath = args.Length > 1 ? args[1] : null;

// Klasör verildiyse içinden .sln/.csproj bul.
if (Directory.Exists(inputPath))
{
    var found = MsBuildSolutionLoader.FindSolutionOrProject(inputPath);
    if (found is null)
    {
        Console.Error.WriteLine($"'{inputPath}' içinde .sln veya .csproj bulunamadı.");
        return 1;
    }
    inputPath = found;
}

var progress = new Progress<string>(message => Console.Error.WriteLine($"  → {message}"));

Console.Error.WriteLine($"Analiz başlıyor: {inputPath}");
var started = DateTime.UtcNow;

AnalysisResult result;
try
{
    result = await MsBuildSolutionLoader.AnalyzeAsync(inputPath, progress: progress);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Analiz başarısız: {ex.Message}");
    return 2;
}

var elapsed = DateTime.UtcNow - started;

var json = JsonSerializer.Serialize(new
{
    AnalyzedPath = inputPath,
    AnalyzedAtUtc = started,
    DurationSeconds = Math.Round(elapsed.TotalSeconds, 1),
    Summary = new
    {
        TotalFindings = result.Findings.Count,
        ByRule = result.Findings.GroupBy(f => f.RuleName).ToDictionary(g => g.Key, g => g.Count()),
        BySeverity = result.Findings.GroupBy(f => f.Severity.ToString()).ToDictionary(g => g.Key, g => g.Count()),
        ClassCount = result.Classes.Count,
        DependencyEdgeCount = result.Dependencies.Count
    },
    result.Findings,
    result.Dependencies,
    result.Diagnostics
}, new JsonSerializerOptions
{
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter() },
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
});

if (outputPath is not null)
{
    File.WriteAllText(outputPath, json);
    Console.Error.WriteLine($"Rapor yazıldı: {outputPath}");
}
else
{
    Console.WriteLine(json);
}

Console.Error.WriteLine(
    $"Bitti ({elapsed.TotalSeconds:F1} sn): {result.Findings.Count} bulgu, " +
    $"{result.Classes.Count} sınıf, {result.Dependencies.Count} bağımlılık kenarı, " +
    $"{result.Diagnostics.Count} tanı mesajı.");

return 0;
