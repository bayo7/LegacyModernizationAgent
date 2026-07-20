using LegacyModernizationAgent.Analyzer.Abstractions;
using LegacyModernizationAgent.Analyzer.Graph;
using LegacyModernizationAgent.Analyzer.Models;
using LegacyModernizationAgent.Analyzer.Rules;
using Microsoft.CodeAnalysis;

namespace LegacyModernizationAgent.Analyzer.Engine;

/// <summary>
/// Analiz orkestratörü: verilen Solution üzerinde tüm dosya-seviyesi ve
/// solution-seviyesi kuralları çalıştırır, bağımlılık grafiğini ve class
/// özetlerini çıkarır. Workspace'in nasıl yüklendiğinden bağımsızdır
/// (testlerde AdhocWorkspace, üretimde MSBuildWorkspace).
/// </summary>
public sealed class AnalysisEngine
{
    private readonly IReadOnlyList<ICodeAnalyzer> _fileAnalyzers;
    private readonly IReadOnlyList<ISolutionAnalyzer> _solutionAnalyzers;

    public AnalysisEngine(AnalyzerThresholds? thresholds = null)
        : this(
            [
                new NPlusOneQueryAnalyzer(),
                new GodClassAnalyzer(thresholds),
                new LongMethodAnalyzer(thresholds)
            ],
            [
                new CircularDependencyAnalyzer(),
                new UnusedCodeAnalyzer()
            ])
    {
    }

    public AnalysisEngine(IReadOnlyList<ICodeAnalyzer> fileAnalyzers, IReadOnlyList<ISolutionAnalyzer> solutionAnalyzers)
    {
        _fileAnalyzers = fileAnalyzers;
        _solutionAnalyzers = solutionAnalyzers;
    }

    public async Task<AnalysisResult> AnalyzeAsync(
        Solution solution,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();
        var edges = new HashSet<DependencyEdge>();
        var classes = new List<ClassSummary>();
        var diagnostics = new List<string>();

        foreach (var project in solution.Projects)
        {
            progress?.Report($"Proje analiz ediliyor: {project.Name}");

            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
            {
                diagnostics.Add($"'{project.Name}' projesi derlenemedi, atlandı.");
                continue;
            }

            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                if (GeneratedCodeDetector.IsGeneratedFile(syntaxTree.FilePath))
                    continue;

                var root = await syntaxTree.GetRootAsync(cancellationToken);
                var semanticModel = compilation.GetSemanticModel(syntaxTree);

                foreach (var analyzer in _fileAnalyzers)
                {
                    try
                    {
                        findings.AddRange(analyzer.Analyze(root, semanticModel, syntaxTree.FilePath));
                    }
                    catch (Exception ex)
                    {
                        // Tek bir kuralın tek bir dosyada patlaması tüm analizi durdurmamalı.
                        diagnostics.Add($"{analyzer.RuleName} kuralı '{syntaxTree.FilePath}' dosyasında hata verdi: {ex.Message}");
                    }
                }
            }

            foreach (var edge in DependencyGraphBuilder.Build(compilation))
                edges.Add(edge);

            classes.AddRange(ClassSummaryExtractor.Extract(compilation));
        }

        foreach (var analyzer in _solutionAnalyzers)
        {
            progress?.Report($"Solution geneli kural çalışıyor: {analyzer.RuleName}");
            try
            {
                findings.AddRange(await analyzer.AnalyzeAsync(solution, cancellationToken));
            }
            catch (Exception ex)
            {
                diagnostics.Add($"{analyzer.RuleName} kuralı hata verdi: {ex.Message}");
            }
        }

        return new AnalysisResult(findings, edges.ToList(), classes, diagnostics);
    }
}
