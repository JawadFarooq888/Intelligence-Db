using System.Security.Cryptography.X509Certificates;
using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Infrastructure.Audit;
using IntelligenceDB.Infrastructure.Repository;
using IntelligenceDB.Infrastructure.Security;
using IntelligenceDB.Infrastructure.Targets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IntelligenceDB.Infrastructure;

public static class RepoDbOptions
{
    public static void ConfigureSqlServer(SqlServerDbContextOptionsBuilder sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        sql.MigrationsHistoryTable("__EFMigrationsHistory", "ops");
        sql.EnableRetryOnFailure(maxRetryCount: 3);
        sql.CommandTimeout(60);
    }
}

public static class DependencyInjection
{
    /// <summary>Data Protection application name. API and Worker must use the same value to share keys.</summary>
    public const string DataProtectionAppName = "IntelligenceDB";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Repository");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Repository is not configured. Set it in appsettings or the ConnectionStrings__Repository environment variable.");
        }

        string repoConnection = connectionString;
        void ConfigureDb(DbContextOptionsBuilder o) => o.UseSqlServer(repoConnection, RepoDbOptions.ConfigureSqlServer);

        // Scoped context for request work + a factory for short independent units (audit writes).
        services.AddDbContext<RepoDbContext>(ConfigureDb, ServiceLifetime.Scoped, ServiceLifetime.Singleton);
        services.AddDbContextFactory<RepoDbContext>(ConfigureDb);
        services.AddScoped<IRepoDbContext>(sp => sp.GetRequiredService<RepoDbContext>());

        AddDataProtection(services, configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddSingleton<ITargetConnectionFactory, SqlTargetConnectionFactory>();
        services.AddScoped<IServerProbe, ServerProbe>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        return services;
    }

    /// <summary>
    /// Keys are stored in the repository database (ops.DataProtectionKeys) so the API and Worker share them.
    /// The keys themselves should be encrypted at rest: with a certificate (Docker/Linux) or DPAPI (Windows/IIS).
    /// </summary>
    private static void AddDataProtection(IServiceCollection services, IConfiguration configuration)
    {
        var dp = services.AddDataProtection()
            .SetApplicationName(DataProtectionAppName)
            .PersistKeysToDbContext<RepoDbContext>();

        var certPath = configuration["DataProtection:CertificatePath"];
        if (!string.IsNullOrWhiteSpace(certPath))
        {
            var cert = X509CertificateLoader.LoadPkcs12FromFile(certPath, configuration["DataProtection:CertificatePassword"]);
            dp.ProtectKeysWithCertificate(cert);
        }
        else if (OperatingSystem.IsWindows() && configuration.GetValue<bool>("DataProtection:UseDpapi"))
        {
            dp.ProtectKeysWithDpapi(protectToLocalMachine: true);
        }
    }
}
