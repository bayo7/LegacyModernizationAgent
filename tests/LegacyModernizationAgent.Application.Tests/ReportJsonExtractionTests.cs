using LegacyModernizationAgent.Application.Services;

namespace LegacyModernizationAgent.Application.Tests;

public class ReportJsonExtractionTests
{
    [Fact]
    public void Duz_json_oldugu_gibi_doner()
    {
        var json = """{"summary":"ok","items":[]}""";

        Assert.Equal(json, ReportGenerationService.ExtractJson(json));
    }

    [Fact]
    public void Markdown_citi_icindeki_json_ayiklanir()
    {
        var response = """
            ```json
            {"summary":"ok","items":[]}
            ```
            """;

        Assert.Equal("""{"summary":"ok","items":[]}""", ReportGenerationService.ExtractJson(response));
    }

    [Fact]
    public void Aciklama_metniyle_sarili_json_ayiklanir()
    {
        var response = """
            İşte istediğiniz rapor:
            {"summary":"ok","items":[]}
            Başka bir isteğiniz var mı?
            """;

        Assert.Equal("""{"summary":"ok","items":[]}""", ReportGenerationService.ExtractJson(response));
    }
}
