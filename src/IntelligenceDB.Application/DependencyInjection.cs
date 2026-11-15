using IntelligenceDB.Application.Dashboard;
using IntelligenceDB.Application.Groups;
using IntelligenceDB.Application.Search;
using IntelligenceDB.Application.Servers;
using Microsoft.Extensions.DependencyInjection;

namespace IntelligenceDB.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<TargetSpecBuilder>();
        services.AddScoped<ServerService>();
        services.AddScoped<ServerGroupService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<SearchService>();
        return services;
    }
}
