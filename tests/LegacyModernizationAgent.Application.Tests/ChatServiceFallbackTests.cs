using LegacyModernizationAgent.Application.Services;
using LegacyModernizationAgent.Core.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace LegacyModernizationAgent.Application.Tests;

public class ChatServiceFallbackTests
{
    private static (ChatService Service, FakeChatRepository Chat) CreateService(FakeFindingRepository findings)
    {
        var chat = new FakeChatRepository();
        var service = new ChatService(chat, findings, new UnconfiguredEmbeddingClient(),
            new UnavailableVectorStore(), new UnconfiguredLlmClient(), NullLogger<ChatService>.Instance);
        return (service, chat);
    }

    private static FakeFindingRepository RepositoryWithSummaries(Guid jobId)
    {
        var findings = new FakeFindingRepository();
        findings.Summaries.AddRange(
        [
            new ClassSummary
            {
                AnalysisJobId = jobId, ClassName = "OrderService", Namespace = "Shop.Application",
                FilePath = "Services/OrderService.cs",
                SummaryText = "Class: OrderService\nNamespace: Shop.Application\nPublic Metodlar: void PlaceOrder(int id)"
            },
            new ClassSummary
            {
                AnalysisJobId = jobId, ClassName = "PaymentGateway", Namespace = "Shop.Infrastructure",
                FilePath = "Payments/PaymentGateway.cs",
                SummaryText = "Class: PaymentGateway\nNamespace: Shop.Infrastructure\nPublic Metodlar: bool Charge(decimal amount)"
            }
        ]);
        return findings;
    }

    [Fact]
    public async Task Vektor_aramasi_yokken_anahtar_kelimeyle_ilgili_sinif_bulunur()
    {
        var jobId = Guid.NewGuid();
        var (service, _) = CreateService(RepositoryWithSummaries(jobId));

        var answer = await service.AskAsync(jobId, "PlaceOrder metodu nerede?");

        Assert.Equal("Services/OrderService.cs", Assert.Single(answer.SourceFiles));
        Assert.Contains("OrderService", answer.Content);
        Assert.DoesNotContain("PaymentGateway", answer.Content);
    }

    [Fact]
    public async Task Soru_ve_cevap_gecmise_sirayla_kaydedilir()
    {
        var jobId = Guid.NewGuid();
        var (service, chat) = CreateService(RepositoryWithSummaries(jobId));

        await service.AskAsync(jobId, "PaymentGateway ne yapıyor?");

        Assert.Equal(2, chat.Messages.Count);
        Assert.Equal("user", chat.Messages[0].Role);
        Assert.Equal("PaymentGateway ne yapıyor?", chat.Messages[0].Content);
        Assert.Equal("assistant", chat.Messages[1].Role);
    }

    [Fact]
    public async Task Eslesen_sinif_yoksa_kaynak_listesi_bos_doner()
    {
        var jobId = Guid.NewGuid();
        var (service, _) = CreateService(RepositoryWithSummaries(jobId));

        var answer = await service.AskAsync(jobId, "Kargo takibi hangi sınıfta?");

        Assert.Empty(answer.SourceFiles);
    }
}
