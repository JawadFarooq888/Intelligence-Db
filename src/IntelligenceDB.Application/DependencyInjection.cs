using IntelligenceDB.Application.Agents;
using IntelligenceDB.Application.Dashboard;
using IntelligenceDB.Application.Groups;
using IntelligenceDB.Application.Monitoring;
using IntelligenceDB.Application.Search;
using IntelligenceDB.Application.Servers;
using Microsoft.Extensions.DependencyInjection;

namespace IntelligenceDB.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddOptions<MonitoringOptions>();
        services.AddScoped<TargetSpecBuilder>();
        services.AddScoped<ServerService>();
        services.AddScoped<ServerGroupService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<SearchService>();

        services.AddScoped<CollectionTargetFactory>();
        services.AddScoped<HealthService>();
        services.AddScoped<IngestionService>();
        services.AddScoped<MonitoringQueryService>();
        services.AddScoped<AgentService>();
        return services;
    }
}
