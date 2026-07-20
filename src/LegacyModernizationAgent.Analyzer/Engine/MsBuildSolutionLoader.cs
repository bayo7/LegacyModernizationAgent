using LegacyModernizationAgent.Analyzer.Models;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace LegacyModernizationAgent.Analyzer.Engine;

/// <summary>
/// MSBuildWorkspace ile .sln/.csproj yükler. Kritik detay: MSBuildWorkspace
/// oluşturulmadan ÖNCE MSBuildLocator.RegisterDefaults() çağrılmalıdır — aksi halde
/// workspace sessizce boş proje listesi döndürür (sık yapılan hata, bkz. CLAUDE.md 4.1).
/// </summary>
public static class MsBuildSolutionLoader
{
    private static readonly object RegistrationLock = new();

    public static void EnsureMsBuildRegistered()
    {
        lock (RegistrationLock)
        {
            if (!MSBuildLocator.IsRegistered)
                MSBuildLocator.RegisterDefaults();
        }
    }

    /// <summary>
    /// .sln, .slnx veya .csproj yolu alır; workspace ve solution döndürür.
    /// Workspace dispose edilmeden compilation'lar kullanılabilir olduğundan
    /// çağıran taraf workspace'i dispose etmekten sorumludur.
    /// </summary>
    public static async Task<(Workspace Workspace, Solution Solution, IReadOnlyList<string> LoadDiagnostics)> LoadAsync(
        string path,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMsBuildRegistered();

        if (!File.Exists(path))
            throw new FileNotFoundException($"Solution/proje dosyası bulunamadı: {path}");

        var workspace = MSBuildWorkspace.Create();
        var loadDiagnostics = new List<string>();
        workspace.RegisterWorkspaceFailedHandler(args => loadDiagnostics.Add($"[{args.Diagnostic.Kind}] {args.Diagnostic.Message}"));

        var extension = Path.GetExtension(path).ToLowerInvariant();
        Solution solution;

        if (extension is ".sln" or ".slnx")
        {
            progress?.Report($"Solution yükleniyor: {path}");
            solution = await workspace.OpenSolutionAsync(path, cancellationToken: cancellationToken);
        }
        else if (extension == ".csproj")
        {
            progress?.Report($"Proje yükleniyor: {path}");
            var project = await workspace.OpenProjectAsync(path, cancellationToken: cancellationToken);
            solution = project.Solution;
        }
        else
        {
            throw new ArgumentException($"Desteklenmeyen dosya türü: {extension} (.sln, .slnx veya .csproj bekleniyor)");
        }

        return (workspace, solution, loadDiagnostics);
    }

    /// <summary>
    /// Bir dizin içinden analiz edilecek .sln/.csproj dosyasını bulur:
    /// önce kökte .sln aranır, yoksa tüm alt dizinlerde, en son .csproj'a düşülür.
    /// </summary>
    public static string? FindSolutionOrProject(string directory)
    {
        if (!Directory.Exists(directory))
            return null;

        return Directory.EnumerateFiles(directory, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault()
            ?? Directory.EnumerateFiles(directory, "*.sln", SearchOption.AllDirectories).FirstOrDefault()
            ?? Directory.EnumerateFiles(directory, "*.slnx", SearchOption.AllDirectories).FirstOrDefault()
            ?? Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
    }

    /// <summary>Uçtan uca kolaylık metodu: yol ver, sonucu al.</summary>
    public static async Task<AnalysisResult> AnalyzeAsync(
        string path,
        AnalyzerThresholds? thresholds = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var (workspace, solution, loadDiagnostics) = await LoadAsync(path, progress, cancellationToken);
        using (workspace)
        {
            var engine = new AnalysisEngine(thresholds);
            var result = await engine.AnalyzeAsync(solution, progress, cancellationToken);
            return result with { Diagnostics = [.. loadDiagnostics, .. result.Diagnostics] };
        }
    }
}
