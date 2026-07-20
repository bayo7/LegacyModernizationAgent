using LegacyModernizationAgent.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyModernizationAgent.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAnalysisPipeline, AnalysisPipeline>();
        services.AddScoped<IReportGenerationService, ReportGenerationService>();
        services.AddScoped<ISemanticIndexingService, SemanticIndexingService>();
        services.AddScoped<IChatService, ChatService>();
        return services;
    }
}
