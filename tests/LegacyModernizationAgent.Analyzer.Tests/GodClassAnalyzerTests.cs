using LegacyModernizationAgent.Analyzer.Models;
using LegacyModernizationAgent.Analyzer.Rules;

namespace LegacyModernizationAgent.Analyzer.Tests;

public class GodClassAnalyzerTests
{
    private readonly GodClassAnalyzer _analyzer = new();

    private static string ClassWithPublicMethods(int methodCount, string className = "Sample", string? attribute = null)
    {
        var methods = string.Join("\n", Enumerable.Range(0, methodCount).Select(i => $"    public void Method{i}() {{ }}"));
        return $$"""
            {{attribute ?? ""}}
            public class {{className}}
            {
            {{methods}}
            }
            """;
    }

    [Fact]
    public void On_bes_ve_uzeri_public_metodlu_sinif_bulgu_uretir()
    {
        var (root, model) = RoslynTestHost.Compile(ClassWithPublicMethods(16));

        var finding = Assert.Single(_analyzer.Analyze(root, model, "Test.cs"));

        Assert.Equal("GodClass", finding.RuleName);
        Assert.Equal("Sample", finding.ClassName);
        Assert.Equal(16, finding.Metrics!["PublicMethodCount"]);
    }

    [Fact]
    public void Dort_yuz_satiri_asan_sinif_bulgu_uretir()
    {
        var fields = string.Join("\n", Enumerable.Range(0, 420).Select(i => $"    private int _field{i};"));
        var source = $"public class Big\n{{\n{fields}\n}}";
        var (root, model) = RoslynTestHost.Compile(source);

        var finding = Assert.Single(_analyzer.Analyze(root, model, "Test.cs"));

        Assert.True((int)finding.Metrics!["LineCount"] >= 400);
    }

    [Fact]
    public void Kucuk_sinif_bulgu_uretmez()
    {
        var (root, model) = RoslynTestHost.Compile(ClassWithPublicMethods(5));

        Assert.Empty(_analyzer.Analyze(root, model, "Test.cs"));
    }

    [Fact]
    public void Designer_dosyalari_analiz_disi_birakilir()
    {
        var (root, model) = RoslynTestHost.Compile(ClassWithPublicMethods(30), "Form1.Designer.cs");

        Assert.Empty(_analyzer.Analyze(root, model, "Form1.Designer.cs"));
    }

    [Fact]
    public void Migrations_klasoru_analiz_disi_birakilir()
    {
        var path = Path.Combine("Data", "Migrations", "20240101_Initial.cs").Replace('\\', '/');
        var (root, model) = RoslynTestHost.Compile(ClassWithPublicMethods(30), path);

        Assert.Empty(_analyzer.Analyze(root, model, path));
    }

    [Fact]
    public void GeneratedCode_attribute_tasiyan_sinif_analiz_disi_birakilir()
    {
        var source = ClassWithPublicMethods(30, attribute: """[System.CodeDom.Compiler.GeneratedCode("tool", "1.0")]""");
        var (root, model) = RoslynTestHost.Compile(source);

        Assert.Empty(_analyzer.Analyze(root, model, "Test.cs"));
    }

    [Fact]
    public void Controller_icin_esik_yukseltilir()
    {
        // 16 public metod normal sınıf için bulgu üretir ama controller eşiği (25) altında kalır.
        var (root, model) = RoslynTestHost.Compile(ClassWithPublicMethods(16, "OrdersController"));

        Assert.Empty(_analyzer.Analyze(root, model, "Test.cs"));
    }

    [Fact]
    public void Controller_da_olsa_asiri_buyukse_bulgu_uretir()
    {
        var (root, model) = RoslynTestHost.Compile(ClassWithPublicMethods(30, "OrdersController"));

        var finding = Assert.Single(_analyzer.Analyze(root, model, "Test.cs"));

        Assert.Equal(true, finding.Metrics!["IsController"]);
    }

    [Fact]
    public void Her_iki_esik_asilirsa_yuksek_severity()
    {
        var methods = string.Join("\n", Enumerable.Range(0, 20).Select(i =>
            $"    public void Method{i}()\n    {{\n{string.Join("\n", Enumerable.Range(0, 20).Select(j => $"        var v{j} = {j};"))}\n    }}"));
        var source = $"public class Huge\n{{\n{methods}\n}}";
        var (root, model) = RoslynTestHost.Compile(source);

        var finding = Assert.Single(_analyzer.Analyze(root, model, "Test.cs"));

        Assert.Equal(Severity.High, finding.Severity);
    }
}
