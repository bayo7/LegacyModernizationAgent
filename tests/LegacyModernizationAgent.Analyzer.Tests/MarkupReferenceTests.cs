using LegacyModernizationAgent.Analyzer.Engine;
using LegacyModernizationAgent.Analyzer.Rules;

namespace LegacyModernizationAgent.Analyzer.Tests;

public sealed class MarkupReferenceTests : IDisposable
{
    private readonly string _projectDir = Path.Combine(Path.GetTempPath(), "lma-tests-" + Guid.NewGuid().ToString("N"));

    public MarkupReferenceTests() => Directory.CreateDirectory(_projectDir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_projectDir, recursive: true);
        }
        catch (IOException)
        {
            // Geçici klasör temizlenemezse test sonucu etkilenmez.
        }
    }

    private void WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_projectDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private const string CssSource = """
        public class Css
        {
            public void HideBodyOverflow() { }
            public void NeverCalledAnywhere() { }
        }
        """;

    [Fact]
    public void Razor_ve_cshtml_dosyalarindaki_tanimlayicilar_toplanir()
    {
        WriteFile("Pages/Delete.razor", "@code { async Task Open() { await new Css(JSRuntime).HideBodyOverflow(); } }");
        WriteFile("Views/Index.cshtml", "@Html.Raw(Model.RenderTitle())");

        var identifiers = MarkupReferenceScanner.CollectIdentifiers([_projectDir]);

        Assert.Contains("HideBodyOverflow", identifiers);
        Assert.Contains("RenderTitle", identifiers);
        Assert.DoesNotContain("ShowBodyOverflow", identifiers);
    }

    [Fact]
    public void Derleme_ciktisi_klasorleri_taranmaz()
    {
        WriteFile("obj/Debug/Generated.razor", "@code { void X() { OnlyInBuildOutput(); } }");

        var identifiers = MarkupReferenceScanner.CollectIdentifiers([_projectDir]);

        Assert.DoesNotContain("OnlyInBuildOutput", identifiers);
    }

    [Fact]
    public async Task Yalnizca_razor_dosyasindan_cagrilan_metod_bulgu_uretmez()
    {
        WriteFile("Pages/Delete.razor", "@code { async Task Open() { await new Css(JSRuntime).HideBodyOverflow(); } }");
        var solution = RoslynTestHost.CreateSolutionAt(Path.Combine(_projectDir, "TestProject.csproj"), ("Css.cs", CssSource));

        var findings = await new UnusedCodeAnalyzer().AnalyzeAsync(solution);

        // Razor'da adı geçen metod muaf; hiçbir yerde geçmeyen metod hâlâ raporlanır.
        var finding = Assert.Single(findings);
        Assert.Equal("NeverCalledAnywhere", finding.Metrics!["MethodName"]);
    }
}

public class UnresolvedReferenceTests
{
    [Fact]
    public async Task Cozumlenemeyen_test_attribute_u_olan_metod_bulgu_uretmez()
    {
        // xunit referansı yok: [Fact] çözümlenemez — restore edilmemiş bir repoyu taklit eder.
        var solution = RoslynTestHost.CreateSolution(("Tests.cs", """
            public class BasketTests
            {
                [Fact]
                public void AddsItem() { }
            }
            """));

        var findings = await new UnusedCodeAnalyzer().AnalyzeAsync(solution);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task Cozumlenemeyen_arayuzu_olan_tipin_metodlari_bulgu_uretmez()
    {
        // IHealthCheck tanımlı değil: metodun arayüz üyesi olup olmadığı bilinemez.
        var solution = RoslynTestHost.CreateSolution(("HealthCheck.cs", """
            public class ApiHealthCheck : IHealthCheck
            {
                public int CheckHealth() => 1;
            }
            """));

        var findings = await new UnusedCodeAnalyzer().AnalyzeAsync(solution);

        Assert.Empty(findings);
    }
}

public class ParameterizedTestAttributeTests
{
    [Theory]
    [InlineData("DataTestMethod")]
    [InlineData("TestCase")]
    [InlineData("TestCaseSource")]
    public async Task Parametreli_test_attribute_lari_muaf_tutulur(string attributeName)
    {
        // eShopOnWeb'de [DataTestMethod] ile işaretli bir MSTest metodu yanlışlıkla raporlanıyordu.
        var source = "public class " + attributeName + "Attribute : System.Attribute { }\n" +
                     "public class EndpointTests { [" + attributeName + "] public void Runs() { } }";
        var solution = RoslynTestHost.CreateSolution(("Tests.cs", source));

        var findings = await new UnusedCodeAnalyzer().AnalyzeAsync(solution);

        Assert.Empty(findings);
    }
}
