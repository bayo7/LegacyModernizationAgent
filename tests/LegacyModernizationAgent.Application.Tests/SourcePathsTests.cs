using LegacyModernizationAgent.Application.Services;

namespace LegacyModernizationAgent.Application.Tests;

public class SourcePathsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "workspaces", "job-1");

    [Fact]
    public void Kok_altindaki_dosya_goreli_ve_egik_cizgili_doner()
    {
        var absolute = Path.Combine(Root, "src", "ApplicationCore", "Basket.cs");

        Assert.Equal("src/ApplicationCore/Basket.cs", SourcePaths.ToRelative(Root, absolute));
    }

    [Fact]
    public void Kok_disindaki_dosya_oldugu_gibi_kalir()
    {
        var outside = Path.Combine(Path.GetTempPath(), "workspaces", "job-2", "Other.cs");

        Assert.Equal(outside, SourcePaths.ToRelative(Root, outside));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Services/OrderService.cs")]
    public void Bos_veya_zaten_goreli_yol_degismez(string path)
    {
        Assert.Equal(path, SourcePaths.ToRelative(Root, path));
    }
}
