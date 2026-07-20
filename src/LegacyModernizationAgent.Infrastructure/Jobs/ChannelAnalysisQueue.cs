using System.Threading.Channels;
using LegacyModernizationAgent.Application.Services;
using LegacyModernizationAgent.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LegacyModernizationAgent.Infrastructure.Jobs;

/// <summary>
/// Faz 2 kuyruğu: bellek içi Channel + BackgroundService. Bilinen sınırlamalar
/// (uygulama yeniden başlarsa kuyruk kaybolur, retry yok) CLAUDE.md Bölüm 7'de
/// belgelendi — Faz 3'te Hangfire'a geçiş bu yüzden planlı (Jobs:UseHangfire ayarı).
/// </summary>
public class ChannelAnalysisQueue : IAnalysisQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ChannelReader<Guid> Reader => _channel.Reader;

    public async Task EnqueueAsync(Guid jobId, CancellationToken ct = default) =>
        await _channel.Writer.WriteAsync(jobId, ct);
}

public class AnalysisWorker(
    ChannelAnalysisQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<AnalysisWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var jobId in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                // Pipeline scoped servisler (DbContext) kullandığı için her iş kendi scope'unda çalışır.
                using var scope = scopeFactory.CreateScope();
                var pipeline = scope.ServiceProvider.GetRequiredService<IAnalysisPipeline>();
                await pipeline.RunAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Analiz işçisi {JobId} işini işlerken hata aldı.", jobId);
            }
        }
    }
}
