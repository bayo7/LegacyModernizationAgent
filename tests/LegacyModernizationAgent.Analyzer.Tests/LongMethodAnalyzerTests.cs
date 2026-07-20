using LegacyModernizationAgent.Analyzer.Models;
using LegacyModernizationAgent.Analyzer.Rules;

namespace LegacyModernizationAgent.Analyzer.Tests;

public class LongMethodAnalyzerTests
{
    private readonly LongMethodAnalyzer _analyzer = new();

    private static string MethodWithLines(int bodyLineCount, string methodName = "DoWork")
    {
        var lines = string.Join("\n", Enumerable.Range(0, bodyLineCount).Select(i => $"        var v{i} = {i};"));
        return $$"""
            public class Sample
            {
                public void {{methodName}}()
                {
            {{lines}}
                }
            }
            """;
    }

    [Fact]
    public void Uzun_metod_bulgu_uretir()
    {
        var (root, model) = RoslynTestHost.Compile(MethodWithLines(60));

        var findings = _analyzer.Analyze(root, model, "Test.cs").ToList();

        var finding = Assert.Single(findings);
        Assert.Equal("LongMethod", finding.RuleName);
        Assert.Equal("Sample", finding.ClassName);
        Assert.True((int)finding.Metrics!["LineCount"] >= 50);
        Assert.Equal(1, finding.Metrics["CyclomaticComplexity"]);
    }

    [Fact]
    public void Uzun_ama_duz_akisli_metod_dusuk_severity_alir()
    {
        // 60 satır düz atama: complexity 1 → satır sayısı yanıltıcı bir sinyal, Low beklenir.
        var (root, model) = RoslynTestHost.Compile(MethodWithLines(60));

        var finding = Assert.Single(_analyzer.Analyze(root, model, "Test.cs"));

        Assert.Equal(Severity.Low, finding.Severity);
    }

    [Fact]
    public void Kisa_metod_bulgu_uretmez()
    {
        var (root, model) = RoslynTestHost.Compile(MethodWithLines(10));

        Assert.Empty(_analyzer.Analyze(root, model, "Test.cs"));
    }

    [Fact]
    public void Kisa_ama_cok_karmasik_metod_bulgu_uretir()
    {
        // Satır sayısı düşük ama 16 if bloğu tek satırlarda → complexity 17.
        var conditions = string.Join(" ", Enumerable.Range(0, 16).Select(i => $"if (x == {i}) x++;"));
        var source = $$"""
            public class Sample
            {
                public int Evaluate(int x)
                {
                    {{conditions}}
                    return x;
                }
            }
            """;
        var (root, model) = RoslynTestHost.Compile(source);

        var finding = Assert.Single(_analyzer.Analyze(root, model, "Test.cs"));

        Assert.True((int)finding.Metrics!["CyclomaticComplexity"] >= 15);
        Assert.Equal(Severity.Medium, finding.Severity);
    }

    [Fact]
    public void Uzun_ve_karmasik_metod_yuksek_severity_alir()
    {
        var lines = string.Join("\n", Enumerable.Range(0, 55).Select(i => $"        if (x > {i}) x--;"));
        var source = $$"""
            public class Sample
            {
                public int Evaluate(int x)
                {
            {{lines}}
                    return x;
                }
            }
            """;
        var (root, model) = RoslynTestHost.Compile(source);

        var finding = Assert.Single(_analyzer.Analyze(root, model, "Test.cs"));

        Assert.Equal(Severity.High, finding.Severity);
    }

    [Fact]
    public void Sinirlama_notu_ve_metrikler_her_bulguda_bulunur()
    {
        var (root, model) = RoslynTestHost.Compile(MethodWithLines(60));

        var finding = Assert.Single(_analyzer.Analyze(root, model, "Test.cs"));

        Assert.False(string.IsNullOrWhiteSpace(finding.LimitationNote));
        Assert.Contains("LineCount", finding.Metrics!.Keys);
        Assert.Contains("CyclomaticComplexity", finding.Metrics.Keys);
    }
}
