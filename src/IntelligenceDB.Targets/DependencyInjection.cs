using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Targets.Collectors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IntelligenceDB.Targets;

public static class DependencyInjection
{
    /// <summary>Connections, capability probe, collectors and the collection engine.</summary>
    public static IServiceCollection AddTargets(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ITargetConnectionFactory, SqlTargetConnectionFactory>();
        services.AddSingleton<ServerProbe>();
        services.AddSingleton<IServerProbe>(sp => sp.GetRequiredService<ServerProbe>());

        services.AddSingleton<ICollector, MetricsCollector>();
        services.AddSingleton<ICollector, WaitsCollector>();
        services.AddSingleton<ICollector, FileIoCollector>();
        services.AddSingleton<ICollector, DatabasesCollector>();
        services.AddSingleton<ICollector, BackupsCollector>();
        services.AddSingleton<ICollector, ConfigCollector>();
        services.AddSingleton<CollectionEngine>();
        return services;
    }
}
