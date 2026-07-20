using LegacyModernizationAgent.Analyzer.Models;
using LegacyModernizationAgent.Analyzer.Rules;

namespace LegacyModernizationAgent.Analyzer.Tests;

public class NPlusOneQueryAnalyzerTests
{
    private readonly NPlusOneQueryAnalyzer _analyzer = new();

    // EF Core'a referans vermeden navigation property senaryosunu temsil eden model:
    // kaynak kodda tanımlı entity + kaynak kodda tanımlı entity koleksiyonu.
    private const string EntityModel = """
        using System;
        using System.Collections.Generic;
        using System.Linq;

        public class Order
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public List<string> Tags { get; set; } = new();
            public virtual ICollection<OrderLine> Lines { get; set; } = new List<OrderLine>();
        }

        public class OrderLine
        {
            public int Id { get; set; }
            public decimal Price { get; set; }
        }

        public static class FakeEf
        {
            public static IEnumerable<T> Include<T, TProp>(this IEnumerable<T> source, Func<T, TProp> selector) => source;
        }
        """;

    [Fact]
    public void Dongude_navigation_uzerinde_linq_cagrisi_bulgu_uretir()
    {
        var source = EntityModel + """

            public class ReportService
            {
                public int TotalLines(List<Order> orders)
                {
                    int total = 0;
                    foreach (var order in orders)
                    {
                        total += order.Lines.Count();
                    }
                    return total;
                }
            }
            """;
        var (root, model) = RoslynTestHost.Compile(source);

        var finding = Assert.Single(_analyzer.Analyze(root, model, "Test.cs"));

        Assert.Equal("NPlusOneQuery", finding.RuleName);
        Assert.Equal(Confidence.Medium, finding.Confidence);
        Assert.Equal("Lines", finding.Metrics!["NavigationProperty"]);
    }

    [Fact]
    public void Ic_ice_foreach_ile_navigation_gezmek_bulgu_uretir()
    {
        var source = EntityModel + """

            public class ReportService
            {
                public decimal Total(List<Order> orders)
                {
                    decimal total = 0;
                    foreach (var order in orders)
                    {
                        foreach (var line in order.Lines)
                        {
                            total += line.Price;
                        }
                    }
                    return total;
                }
            }
            """;
        var (root, model) = RoslynTestHost.Compile(source);

        Assert.Single(_analyzer.Analyze(root, model, "Test.cs"));
    }

    [Fact]
    public void Include_ile_yuklenmis_kaynakta_bulgu_uretilmez()
    {
        var source = EntityModel + """

            public class ReportService
            {
                public int TotalLines(IEnumerable<Order> query)
                {
                    var orders = query.Include(o => o.Lines).ToList();
                    foreach (var order in orders)
                    {
                        var count = order.Lines.Count();
                    }
                    return 0;
                }
            }
            """;
        var (root, model) = RoslynTestHost.Compile(source);

        Assert.Empty(_analyzer.Analyze(root, model, "Test.cs"));
    }

    [Fact]
    public void String_property_uzerindeki_linq_cagrisi_bulgu_uretmez()
    {
        var source = EntityModel + """

            public class ReportService
            {
                public int TotalChars(List<Order> orders)
                {
                    int total = 0;
                    foreach (var order in orders)
                    {
                        total += order.Name.Count();
                    }
                    return total;
                }
            }
            """;
        var (root, model) = RoslynTestHost.Compile(source);

        Assert.Empty(_analyzer.Analyze(root, model, "Test.cs"));
    }

    [Fact]
    public void Framework_tipi_koleksiyon_navigation_sayilmaz()
    {
        // Tags: List<string> — eleman tipi kaynak kodda tanımlı bir entity değil.
        var source = EntityModel + """

            public class ReportService
            {
                public int TotalTags(List<Order> orders)
                {
                    int total = 0;
                    foreach (var order in orders)
                    {
                        total += order.Tags.Count();
                    }
                    return total;
                }
            }
            """;
        var (root, model) = RoslynTestHost.Compile(source);

        Assert.Empty(_analyzer.Analyze(root, model, "Test.cs"));
    }

    [Fact]
    public void Ayni_navigation_icin_dongu_basina_tek_bulgu_uretilir()
    {
        var source = EntityModel + """

            public class ReportService
            {
                public int Stats(List<Order> orders)
                {
                    int total = 0;
                    foreach (var order in orders)
                    {
                        total += order.Lines.Count();
                        var any = order.Lines.Any();
                        var first = order.Lines.FirstOrDefault();
                    }
                    return total;
                }
            }
            """;
        var (root, model) = RoslynTestHost.Compile(source);

        Assert.Single(_analyzer.Analyze(root, model, "Test.cs"));
    }
}
