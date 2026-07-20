using Hangfire;
using LegacyModernizationAgent.Application.Services;
using LegacyModernizationAgent.Core.Abstractions;

namespace LegacyModernizationAgent.Infrastructure.Jobs;

/// <summary>
/// Faz 3 kuyruğu: Hangfire — kalıcı kuyruk (uygulama yeniden başlasa da işler kaybolmaz)
/// + otomatik retry (LLM/embedding API'lerinin geçici hataları için) + /hangfire dashboard'u.
/// IHostedService'ten geçiş gerekçesi CLAUDE.md Bölüm 7'de.
/// </summary>
public class HangfireAnalysisQueue(IBackgroundJobClient jobClient) : IAnalysisQueue
{
    public Task EnqueueAsync(Guid jobId, CancellationToken ct = default)
    {
        jobClient.Enqueue<IAnalysisPipeline>(pipeline => pipeline.RunAsync(jobId, CancellationToken.None));
        return Task.CompletedTask;
    }
}
