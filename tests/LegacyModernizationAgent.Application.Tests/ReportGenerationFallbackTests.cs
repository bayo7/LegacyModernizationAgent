using System.Text.Json.Nodes;
using LegacyModernizationAgent.Application.Services;
using LegacyModernizationAgent.Core.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace LegacyModernizationAgent.Application.Tests;

public class ReportGenerationFallbackTests
{
    private static ReportGenerationService CreateService(FakeFindingRepository findings, FakeReportRepository reports) =>
        new(findings, reports, new UnconfiguredLlmClient(), new UnconfiguredEmbeddingClient(),
            new UnavailableVectorStore(), NullLogger<ReportGenerationService>.Instance);

    [Fact]
    public async Task Llm_yokken_kural_tabanli_rapor_uretilir_ve_kaydedilir()
    {
        var jobId = Guid.NewGuid();
        var findings = new FakeFindingRepository();
        var reports = new FakeReportRepository();
        findings.Findings.Add(new Finding
        {
            AnalysisJobId = jobId, RuleName = "LongMethod", Severity = "Medium", Confidence = "High",
            ClassName = "OrderService", FilePath = "Services/OrderService.cs", Description = "Metod uzun."
        });

        var report = await CreateService(findings, reports).GenerateAsync(jobId);

        var json = JsonNode.Parse(report.ReportJson)!;
        Assert.Equal("offline-fallback", json["generator"]!.GetValue<string>());
        Assert.Equal(1, json["findingCount"]!.GetValue<int>());
        Assert.Equal(jobId.ToString(), json["analysisJobId"]!.GetValue<string>());
        Assert.Single(json["items"]!.AsArray());
        Assert.Single(reports.Reports);
    }

    [Fact]
    public async Task Oncelik_severity_ve_confidence_birlikte_hesaplanir()
    {
        var jobId = Guid.NewGuid();
        var findings = new FakeFindingRepository();
        var lowConfidenceHigh = new Finding
        {
            AnalysisJobId = jobId, RuleName = "UnusedCode", Severity = "High", Confidence = "Low",
            ClassName = "Legacy", FilePath = "Legacy.cs", Description = "Referans yok."
        };
        var confidentHigh = new Finding
        {
            AnalysisJobId = jobId, RuleName = "CircularDependency", Severity = "High", Confidence = "High",
            ClassName = "A", FilePath = "A.cs", Description = "Katmanlar arası döngü."
        };
        var low = new Finding
        {
            AnalysisJobId = jobId, RuleName = "LongMethod", Severity = "Low", Confidence = "High",
            ClassName = "Mapper", FilePath = "Mapper.cs", Description = "Uzun ama düz."
        };
        findings.Findings.AddRange([low, lowConfidenceHigh, confidentHigh]);

        var report = await CreateService(findings, new FakeReportRepository()).GenerateAsync(jobId);

        var items = JsonNode.Parse(report.ReportJson)!["items"]!.AsArray();
        Assert.Equal(3, items.Count);

        // En önce: yüksek önem + yüksek güven
        Assert.Equal(confidentHigh.Id.ToString(), items[0]!["findingId"]!.GetValue<string>());
        Assert.Equal("Yüksek", items[0]!["priority"]!.GetValue<string>());

        // Yüksek önem ama düşük güven → Orta'ya iner
        Assert.Equal(lowConfidenceHigh.Id.ToString(), items[1]!["findingId"]!.GetValue<string>());
        Assert.Equal("Orta", items[1]!["priority"]!.GetValue<string>());

        Assert.Equal("Düşük", items[2]!["priority"]!.GetValue<string>());
    }

    [Fact]
    public async Task Bulgu_yoksa_bos_rapor_uretilir()
    {
        var jobId = Guid.NewGuid();

        var report = await CreateService(new FakeFindingRepository(), new FakeReportRepository()).GenerateAsync(jobId);

        var json = JsonNode.Parse(report.ReportJson)!;
        Assert.Empty(json["items"]!.AsArray());
        Assert.Equal(0, json["findingCount"]!.GetValue<int>());
    }
}
