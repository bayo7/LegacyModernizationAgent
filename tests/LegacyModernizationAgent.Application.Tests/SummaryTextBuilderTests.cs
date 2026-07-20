using LegacyModernizationAgent.Application.Services;
using AnalyzerModels = LegacyModernizationAgent.Analyzer.Models;
using CoreEntities = LegacyModernizationAgent.Core.Entities;

namespace LegacyModernizationAgent.Application.Tests;

public class SummaryTextBuilderTests
{
    [Fact]
    public void Sablon_tum_alanlari_icerir()
    {
        var summary = new AnalyzerModels.ClassSummary(
            "OrderService",
            "Shop.Application",
            "Services/OrderService.cs",
            ["void PlaceOrder(int id)", "Task<bool> CancelAsync(int id)"],
            "Sipariş işlemlerini yönetir.");

        var finding = new CoreEntities.Finding
        {
            RuleName = "GodClass",
            Severity = "Medium",
            Description = "Sınıf çok sorumluluk taşıyor olabilir."
        };

        var text = SummaryTextBuilder.Build(summary, [finding]);

        Assert.Contains("Class: OrderService", text);
        Assert.Contains("Namespace: Shop.Application", text);
        Assert.Contains("PlaceOrder", text);
        Assert.Contains("XML Doc: Sipariş işlemlerini yönetir.", text);
        Assert.Contains("[GodClass/Medium]", text);
    }

    [Fact]
    public void Bulgu_yoksa_bulgu_bolumu_yazilmaz()
    {
        var summary = new AnalyzerModels.ClassSummary("Empty", "Ns", "Empty.cs", [], null);

        var text = SummaryTextBuilder.Build(summary, []);

        Assert.DoesNotContain("İlişkili Bulgular", text);
        Assert.DoesNotContain("XML Doc", text);
    }
}
