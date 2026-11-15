using IntelligenceDB.Application.Monitoring;
using IntelligenceDB.Domain.Agents;
using IntelligenceDB.Infrastructure.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntelligenceDB.Infrastructure.Monitoring;

/// <summary>
/// Deletes history older than the configured retention, in small batches so the repository
/// never holds long locks or grows its log much.
/// </summary>
public sealed class RetentionService(
    RepoDbContext db,
    IOptions<MonitoringOptions> options,
    TimeProvider clock,
    ILogger<RetentionService> logger)
{
    private const int BatchSize = 5000;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var r = options.Value.Retention;
        var now = clock.GetUtcNow().UtcDateTime;
        int total = 0;

        total += await DeleteBatchedAsync(() => db.ServerMetrics.Where(x => x.CollectedUtc < now.AddDays(-r.MetricsDays)).OrderBy(x => x.CollectedUtc), ct);
        total += await DeleteBatchedAsync(() => db.WaitStats.Where(x => x.CollectedUtc < now.AddDays(-r.WaitsDays)).OrderBy(x => x.CollectedUtc), ct);
        total += await DeleteBatchedAsync(() => db.FileIoStats.Where(x => x.CollectedUtc < now.AddDays(-r.FileIoDays)).OrderBy(x => x.CollectedUtc), ct);
        total += await DeleteBatchedAsync(() => db.VolumeStats.Where(x => x.CollectedUtc < now.AddDays(-r.VolumesDays)).OrderBy(x => x.CollectedUtc), ct);
        total += await DeleteBatchedAsync(() => db.DatabaseSizeSamples.Where(x => x.CollectedUtc < now.AddDays(-r.DatabaseSizeDays)).OrderBy(x => x.CollectedUtc), ct);
        total += await DeleteBatchedAsync(() => db.BackupRecords.Where(x => x.FinishUtc < now.AddDays(-r.BackupsDays)).OrderBy(x => x.FinishUtc), ct);
        total += await DeleteBatchedAsync(() => db.CollectionRuns.Where(x => x.StartedUtc < now.AddDays(-r.CollectionRunsDays)).OrderBy(x => x.StartedUtc), ct);

        // Agent commands: expire stale ones (and drop their encrypted payload), delete old ones.
        await db.AgentCommands
            .Where(c => c.Status != AgentCommandStatus.Completed && c.Status != AgentCommandStatus.Expired && c.ExpiresUtc < now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, AgentCommandStatus.Expired)
                .SetProperty(c => c.ProtectedPayload, (byte[]?)null), ct);
        total += await db.AgentCommands.Where(c => c.CreatedUtc < now.AddDays(-1)).ExecuteDeleteAsync(ct);

        if (total > 0) logger.LogInformation("Retention removed {Rows} old rows", total);
        return total;
    }

    private static async Task<int> DeleteBatchedAsync<T>(Func<IQueryable<T>> query, CancellationToken ct)
    {
        int total = 0;
        while (true)
        {
            int n = await query().Take(BatchSize).ExecuteDeleteAsync(ct);
            total += n;
            if (n < BatchSize) return total;
        }
    }
}
