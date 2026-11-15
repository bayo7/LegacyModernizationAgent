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
