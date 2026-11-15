using System.Security.Cryptography;
using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Application.Monitoring;
using IntelligenceDB.Infrastructure.Monitoring;
using IntelligenceDB.Targets;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace IntelligenceDB.Worker.Jobs;

/// <summary>
/// Loads the servers the portal reaches directly, runs the due collectors and stores the results.
/// Quartz never runs two instances at once, so a slow tick simply delays the next one.
/// </summary>
[DisallowConcurrentExecution]
public sealed class CollectionJob(
    IServiceScopeFactory scopes,
    CollectionEngine engine,
    IConfiguration config,
    ILogger<CollectionJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var ct = context.CancellationToken;
        int parallel = config.GetValue("Worker:MaxParallelServers", 16);

        try
        {
            using (var scope = scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IRepoDbContext>();
                var factory = scope.ServiceProvider.GetRequiredService<CollectionTargetFactory>();

                var servers = await db.Servers.AsNoTracking()
                    .Include(s => s.MonitoringCredential)
                    .Include(s => s.AdminCredential)
                    .Include(s => s.Capabilities)
                    .Where(s => s.IsEnabled && s.AgentId == null)
                    .AsSplitQuery()
                    .ToListAsync(ct);

                var targets = new List<CollectionTarget>();
                foreach (var s in servers)
                {
                    try
                    {
                        targets.Add(factory.Create(s));
                    }
                    catch (CryptographicException ex)
                    {
                        logger.LogError(ex, "Cannot decrypt the credentials of server {ServerId}; it is skipped", s.Id);
                    }
                }

                engine.UpdateTargets(targets);
            }

            var batches = await engine.RunDueAsync(parallel, ct);
            if (batches.Count == 0) return;

            using (var scope = scopes.CreateScope())
            {
                var ingestion = scope.ServiceProvider.GetRequiredService<IngestionService>();
                await ingestion.IngestAsync(batches, "worker", null, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            // Never let one failing tick stop the scheduler (e.g. repository briefly unavailable).
            logger.LogError(ex, "Collection tick failed");
        }
    }
}

[DisallowConcurrentExecution]
public sealed class RetentionJob(IServiceScopeFactory scopes, ILogger<RetentionJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<RetentionService>().RunAsync(context.CancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Retention job failed");
        }
    }
}
