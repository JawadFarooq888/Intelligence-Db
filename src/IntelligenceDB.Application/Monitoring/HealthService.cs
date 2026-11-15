using System.Text.Json;
using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Application.Capabilities;
using IntelligenceDB.Domain.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IntelligenceDB.Application.Monitoring;

/// <summary>Gathers the latest facts for one server and stores its health score with the breakdown.</summary>
public sealed class HealthService(IRepoDbContext db, IOptions<MonitoringOptions> options, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<HealthResult> RecalculateAsync(int serverId, CancellationToken ct)
    {
        var o = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var facts = await LoadFactsAsync(serverId, now, ct);
        var result = HealthScoreCalculator.Calculate(facts.Inputs, o.Thresholds);

        var row = await db.ServerHealth.FirstOrDefaultAsync(h => h.ServerId == serverId, ct);
        if (row is null)
        {
            row = new ServerHealth { ServerId = serverId };
            db.ServerHealth.Add(row);
        }

        row.Score = result.Score;
        row.BreakdownJson = JsonSerializer.Serialize(result.Deductions, Json);
        row.CalculatedUtc = now;
        await db.SaveChangesAsync(ct);
        return result;
    }

    public static IReadOnlyList<HealthDeduction> ParseBreakdown(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<HealthDeduction>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal sealed record ServerFacts(HealthInputs Inputs, IReadOnlyList<AdviceItem> ConfigAdvice, IReadOnlyList<Database> Databases);

    /// <summary>Loads everything the rules need. Also used by the overview page so both show the same findings.</summary>
    internal async Task<ServerFacts> LoadFactsAsync(int serverId, DateTime now, CancellationToken ct)
    {
        var o = options.Value;
        var server = await db.Servers.AsNoTracking()
            .Include(s => s.Info)
            .Include(s => s.Capabilities)
            .FirstAsync(s => s.Id == serverId, ct);

        var since = now.AddMinutes(-15);
        var metrics = await db.ServerMetrics.AsNoTracking()
            .Where(m => m.ServerId == serverId && m.CollectedUtc >= since)
            .OrderBy(m => m.CollectedUtc)
            .ToListAsync(ct);
        var latest = metrics.LastOrDefault();
        var cpuValues = metrics.Where(m => m.SqlCpuPct is not null).Select(m => m.SqlCpuPct!.Value).ToList();

        var volumes = await LatestVolumesAsync(serverId, ct);

        var dbs = await db.Databases.AsNoTracking()
            .Include(d => d.Files)
            .Where(d => d.ServerId == serverId && !d.IsDropped)
            .ToListAsync(ct);

        bool backupHistory = server.Capabilities.Any(c => c.CapabilityKey == CapabilityKeys.BackupHistory && c.IsSupported);
        int maxCompat = MaxCompatibilityLevel(server.Info);
        var dbAdvice = dbs.SelectMany(d => DatabaseAdvisor.Evaluate(d, backupHistory, o.Thresholds, now, maxCompat)).ToList();

        var config = await db.ServerConfigSettings.AsNoTracking()
            .Where(c => c.ServerId == serverId)
            .ToDictionaryAsync(c => c.Name, c => c.ValueInUse, StringComparer.OrdinalIgnoreCase, ct);
        var tempdbFiles = dbs.FirstOrDefault(d => d.Name == "tempdb")?.Files ?? [];
        var configAdvice = ConfigAdvisor.Evaluate(config, server.Info, tempdbFiles, server.Info is null ? null : o.LatestFor(server.Info.MajorVersion));

        var inputs = new HealthInputs(
            server.LastStatus,
            cpuValues.Count == 0 ? null : cpuValues.Average(),
            latest?.PageLifeExpectancy,
            latest?.MemoryGrantsPending,
            latest?.BlockedSessions,
            volumes,
            dbAdvice,
            configAdvice);

        return new ServerFacts(inputs, configAdvice, dbs);
    }

    internal async Task<IReadOnlyList<VolumeState>> LatestVolumesAsync(int serverId, CancellationToken ct)
    {
        var lastTime = await db.VolumeStats.Where(v => v.ServerId == serverId).MaxAsync(v => (DateTime?)v.CollectedUtc, ct);
        if (lastTime is null) return [];
        return await db.VolumeStats.AsNoTracking()
            .Where(v => v.ServerId == serverId && v.CollectedUtc == lastTime)
            .Select(v => new VolumeState(v.MountPoint, v.TotalMb, v.FreeMb))
            .ToListAsync(ct);
    }

    /// <summary>Highest compatibility level the engine supports (e.g. 160 for SQL Server 2022).</summary>
    internal static int MaxCompatibilityLevel(ServerInfo? info) => info switch
    {
        null => 0,
        { EngineEdition: 5 or 8 } => 160,
        { MajorVersion: >= 13 } => info.MajorVersion * 10,
        _ => 0,
    };
}
