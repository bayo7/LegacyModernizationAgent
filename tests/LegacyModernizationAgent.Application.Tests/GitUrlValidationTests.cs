using LegacyModernizationAgent.Infrastructure.Sources;

namespace LegacyModernizationAgent.Application.Tests;

public class GitUrlValidationTests
{
    [Theory]
    [InlineData("https://github.com/dotnet/eShopOnWeb")]
    [InlineData("https://github.com/dotnet/eShopOnWeb.git")]
    [InlineData("http://git.local:3000/team/repo.git")]
    [InlineData("git@github.com:dotnet/eShopOnWeb.git")]
    public void Gecerli_git_urlleri_kabul_edilir(string url)
    {
        Assert.True(GitSourcePreparer.LooksLikeGitUrl(url));
        Assert.True(GitSourcePreparer.IsSafeGitUrl(url));
    }

    [Theory]
    [InlineData("https://github.com/org/repo --upload-pack=calc")]
    [InlineData("https://github.com/org/repo\" --config core.sshCommand=calc \"")]
    [InlineData("git@github.com:org/repo.git -oProxyCommand=calc")]
    [InlineData("git@-oProxyCommand=calc:org/repo")]
    [InlineData("https://")]
    public void Arguman_enjeksiyonu_iceren_urller_reddedilir(string url)
    {
        Assert.False(GitSourcePreparer.IsSafeGitUrl(url));
    }

    [Theory]
    [InlineData("C:/repos/Proje/Proje.sln")]
    [InlineData("/home/user/repos/Proje")]
    [InlineData("Proje.csproj")]
    public void Lokal_yollar_git_urli_sayilmaz(string path)
    {
        Assert.False(GitSourcePreparer.LooksLikeGitUrl(path));
    }
}
