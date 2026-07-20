using LegacyModernizationAgent.Analyzer.Models;
using LegacyModernizationAgent.Analyzer.Rules;

namespace LegacyModernizationAgent.Analyzer.Tests;

public class CircularDependencyAnalyzerTests
{
    private readonly CircularDependencyAnalyzer _analyzer = new();

    [Fact]
    public void FindCycles_basit_donguyu_bulur()
    {
        var edges = new[]
        {
            new DependencyEdge("A", "B"),
            new DependencyEdge("B", "A"),
            new DependencyEdge("B", "C") // C döngüde değil
        };

        var cycles = CircularDependencyAnalyzer.FindCycles(edges);

        var cycle = Assert.Single(cycles);
        Assert.Equal(new[] { "A", "B" }, cycle.OrderBy(x => x));
    }

    [Fact]
    public void FindCycles_dongusuz_grafikte_bos_doner()
    {
        var edges = new[]
        {
            new DependencyEdge("A", "B"),
            new DependencyEdge("B", "C")
        };

        Assert.Empty(CircularDependencyAnalyzer.FindCycles(edges));
    }

    [Fact]
    public async Task Ayni_katmandaki_karsilikli_referans_dusuk_severity_alir()
    {
        // Order ↔ OrderLine: domain modelinde doğal parent-child ilişkisi.
        var solution = RoslynTestHost.CreateSolution(("Domain.cs", """
            using System.Collections.Generic;

            namespace Shop.Domain
            {
                public class Order
                {
                    public List<OrderLine> Lines { get; set; } = new();
                }

                public class OrderLine
                {
                    public Order Parent { get; set; } = null!;
                }
            }
            """));

        var findings = await _analyzer.AnalyzeAsync(solution);

        var finding = Assert.Single(findings);
        Assert.Equal(Severity.Low, finding.Severity);
        Assert.Equal(false, finding.Metrics!["IsCrossLayer"]);
    }

    [Fact]
    public async Task Katmanlar_arasi_dongu_yuksek_severity_alir()
    {
        var solution = RoslynTestHost.CreateSolution(("Layers.cs", """
            namespace Shop.Application
            {
                public class OrderService
                {
                    public Shop.Infrastructure.OrderRepository Repo { get; set; } = null!;
                }
            }

            namespace Shop.Infrastructure
            {
                public class OrderRepository
                {
                    public Shop.Application.OrderService Service { get; set; } = null!;
                }
            }
            """));

        var findings = await _analyzer.AnalyzeAsync(solution);

        var finding = Assert.Single(findings);
        Assert.Equal(Severity.High, finding.Severity);
        Assert.Equal(true, finding.Metrics!["IsCrossLayer"]);
    }

    [Fact]
    public async Task Dongusuz_kod_bulgu_uretmez()
    {
        var solution = RoslynTestHost.CreateSolution(("Clean.cs", """
            namespace Shop.Domain
            {
                public class Customer { }

                public class Order
                {
                    public Customer Buyer { get; set; } = null!;
                }
            }
            """));

        Assert.Empty(await _analyzer.AnalyzeAsync(solution));
    }
}
