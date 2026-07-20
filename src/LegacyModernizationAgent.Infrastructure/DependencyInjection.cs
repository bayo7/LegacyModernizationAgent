using Hangfire;
using Hangfire.Storage.SQLite;
using LegacyModernizationAgent.Core.Abstractions;
using LegacyModernizationAgent.Infrastructure.Ai;
using LegacyModernizationAgent.Infrastructure.Data;
using LegacyModernizationAgent.Infrastructure.Jobs;
using LegacyModernizationAgent.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyModernizationAgent.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // --- Veritabanı (varsayılan SQLite; SQL Server'a geçiş için connection string yeterli) ---
        var connectionString = configuration.GetConnectionString("Default") ?? "Data Source=data/legacymod.db";
        EnsureSqliteDirectory(connectionString);
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));

        services.AddScoped<IAnalysisJobRepository, EfAnalysisJobRepository>();
        services.AddScoped<IFindingRepository, EfFindingRepository>();
        services.AddScoped<IReportRepository, EfReportRepository>();
        services.AddScoped<IChatRepository, EfChatRepository>();

        // --- AI istemcileri ---
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.Configure<QdrantOptions>(configuration.GetSection(QdrantOptions.SectionName));
        services.AddHttpClient<ILlmClient, OpenAiCompatibleLlmClient>(c => c.Timeout = TimeSpan.FromMinutes(3));
        services.AddHttpClient<IEmbeddingClient, OpenAiCompatibleEmbeddingClient>(c => c.Timeout = TimeSpan.FromMinutes(2));
        services.AddHttpClient<IVectorStore, QdrantHttpVectorStore>(c => c.Timeout = TimeSpan.FromMinutes(2));

        // --- Kaynak hazırlama ---
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddScoped<ISourcePreparer, GitSourcePreparer>();

        // --- İş kuyruğu: Faz 2 = Channel/IHostedService, Faz 3 = Hangfire (kalıcı + retry) ---
        if (configuration.GetValue("Jobs:UseHangfire", defaultValue: true))
        {
            services.AddHangfire(config => config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseSQLiteStorage(configuration.GetValue("Jobs:HangfireDb", "data/hangfire.db")));
            services.AddHangfireServer(o => o.WorkerCount = 1); // Roslyn analizi CPU/IO yoğun; tek işçi yeterli
            services.AddScoped<IAnalysisQueue, HangfireAnalysisQueue>();
        }
        else
        {
            services.AddSingleton<ChannelAnalysisQueue>();
            services.AddSingleton<IAnalysisQueue>(sp => sp.GetRequiredService<ChannelAnalysisQueue>());
            services.AddHostedService<AnalysisWorker>();
        }

        return services;
    }

    private static void EnsureSqliteDirectory(string connectionString)
    {
        var dataSource = connectionString.Split('=', 2) is [_, var path] ? path.Trim() : null;
        var directory = dataSource is null ? null : Path.GetDirectoryName(dataSource);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }
}
