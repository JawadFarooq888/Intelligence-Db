using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Application.Capabilities;
using IntelligenceDB.Application.Common;
using IntelligenceDB.Application.Servers;
using IntelligenceDB.Domain.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IntelligenceDB.Application.Monitoring;

public sealed record HealthDto(int Score, IReadOnlyList<HealthDeduction> Deductions, DateTime CalculatedUtc);

public sealed record MetricPointDto(
    DateTime Time, double? SqlCpuPct, double? OtherCpuPct, long? PageLifeExpectancy, int? MemoryGrantsPending,
    long? TotalServerMemoryMb, long? TargetServerMemoryMb, double? BatchRequestsPerSec, int? UserConnections, int? BlockedSessions);

public sealed record ConfigSettingDto(string Name, long Value, long ValueInUse, bool IsAdvanced, bool IsDynamic, string? Description, bool PendingRestart);

public sealed record VolumeDto(string MountPoint, string? Label, long TotalMb, long FreeMb, double FreePct);

public sealed record DatabaseSummaryDto(int Total, int Online, int WithCritical, int WithWarning, double TotalDataMb, double TotalLogMb);

public sealed record CollectorStatusDto(string Collector, DateTime LastRunUtc, bool Success, string? Error, int DurationMs, string Source);

public sealed record ServerOverviewDto(
    HealthDto? Health,
    MetricPointDto? Latest,
    IReadOnlyList<AdviceItem> ConfigAdvice,
    IReadOnlyList<ConfigSettingDto> Settings,
    IReadOnlyList<VolumeDto> Volumes,
    DatabaseSummaryDto Databases,
    IReadOnlyList<CollectorStatusDto> Collectors,
    bool VolumesSupported);

public sealed record WaitRowDto(
    string WaitType, string Category, string Explanation, long WaitTimeMs, long SignalWaitMs, long WaitingTasks,
    double AvgMsPerWait, double Percent);

public sealed record FileLatencyDto(
    string DatabaseName, int FileId, long Reads, long Writes, double AvgReadMs, double AvgWriteMs, double MbRead, double MbWritten);

public sealed record PerformanceDto(int Hours, int BucketMinutes, IReadOnlyList<MetricPointDto> Series, IReadOnlyList<WaitRowDto> Waits, IReadOnlyList<FileLatencyDto> Files);

public sealed record DatabaseRowDto(
    int Id, int ServerId, string ServerName, string Name, string State, string RecoveryModel, int CompatibilityLevel,
    string? Owner, bool IsReadOnly, double? DataSizeMb, double? DataUsedMb, double? LogSizeMb, double? LogUsedMb,
    int? VlfCount, string? LogReuseWait, DateTime? LastFullBackupUtc, DateTime? LastDiffBackupUtc, DateTime? LastLogBackupUtc,
    DateTime? LastGoodCheckDbUtc, bool CheckDbNever, int CriticalCount, int WarningCount, int InfoCount,
    DateTime LastSeenUtc, bool IsDropped, bool BackupHistoryAvailable);

public sealed record DatabaseFileDto(
    int FileId, string LogicalName, string? PhysicalName, string TypeDesc, double SizeMb, double? UsedMb, double? MaxSizeMb,
    bool IsPercentGrowth, double GrowthValue, string? VolumeMountPoint);

public sealed record SizePointDto(DateTime Day, double DataSizeMb, double? DataUsedMb, double LogSizeMb, double? LogUsedMb);

public sealed record BackupRowDto(
    long Id, string BackupType, DateTime StartUtc, DateTime FinishUtc, int DurationSeconds, long SizeBytes,
    long? CompressedBytes, bool IsCopyOnly, string? PhysicalDevice);

public sealed record DatabaseDetailDto(
    DatabaseRowDto Database, IReadOnlyList<DatabaseFileDto> Files, IReadOnlyList<AdviceItem> Advice,
    IReadOnlyList<SizePointDto> SizeHistory, IReadOnlyList<BackupRowDto> Backups);

/// <summary>Read models for the Server Overview, Performance and Databases pages.</summary>
public sealed class MonitoringQueryService(
    IRepoDbContext db,
    ICurrentUser user,
    HealthService health,
    IOptions<MonitoringOptions> options,
    TimeProvider clock)
{
    public async Task<ServerOverviewDto> GetOverviewAsync(int serverId, CancellationToken ct)
    {
        await EnsureVisibleAsync(serverId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var facts = await health.LoadFactsAsync(serverId, now, ct);

        var h = await db.ServerHealth.AsNoTracking().FirstOrDefaultAsync(x => x.ServerId == serverId, ct);
        var latest = await db.ServerMetrics.AsNoTracking()
            .Where(m => m.ServerId == serverId)
            .OrderByDescending(m => m.CollectedUtc)
            .FirstOrDefaultAsync(ct);

        var settings = await db.ServerConfigSettings.AsNoTracking()
            .Where(c => c.ServerId == serverId)
            .OrderBy(c => c.Name)
            .Select(c => new ConfigSettingDto(c.Name, c.Value, c.ValueInUse, c.IsAdvanced, c.IsDynamic, c.Description, c.Value != c.ValueInUse))
            .ToListAsync(ct);

        var volumes = (await health.LatestVolumesAsync(serverId, ct))
            .Select(v => new VolumeDto(v.MountPoint, null, v.TotalMb, v.FreeMb, v.TotalMb > 0 ? Math.Round(100.0 * v.FreeMb / v.TotalMb, 1) : 0))
            .OrderBy(v => v.MountPoint)
            .ToList();

        var dbAdvice = facts.Inputs.DatabaseAdvice;
        var summary = new DatabaseSummaryDto(
            facts.Databases.Count,
            facts.Databases.Count(d => d.State == "ONLINE"),
            dbAdvice.Where(a => a.Severity == AdviceSeverity.Critical).Select(a => a.Target).Distinct().Count(),
            dbAdvice.Where(a => a.Severity == AdviceSeverity.Warning).Select(a => a.Target).Distinct().Count(),
            Math.Round(facts.Databases.Sum(d => d.DataSizeMb ?? 0), 1),
            Math.Round(facts.Databases.Sum(d => d.LogSizeMb ?? 0), 1));

        // Latest run per collector (the table is small: retention keeps a few days).
        var since = now.AddDays(-2);
        var runs = await db.CollectionRuns.AsNoTracking()
            .Where(r => r.ServerId == serverId && r.StartedUtc >= since)
            .OrderByDescending(r => r.StartedUtc)
            .Take(300)
            .ToListAsync(ct);
        var collectors = runs.GroupBy(r => r.Collector)
            .Select(g => g.First())
            .OrderBy(r => r.Collector)
            .Select(r => new CollectorStatusDto(r.Collector, r.StartedUtc, r.Success, r.Error, r.DurationMs, r.Source))
            .ToList();

        bool volumesSupported = await db.ServerCapabilities.AnyAsync(
            c => c.ServerId == serverId && c.CapabilityKey == CapabilityKeys.VolumeStats && c.IsSupported, ct);

        return new ServerOverviewDto(
            h is null ? null : new HealthDto(h.Score, HealthService.ParseBreakdown(h.BreakdownJson), h.CalculatedUtc),
            latest is null ? null : ToPoint(latest.CollectedUtc, [latest]),
            facts.ConfigAdvice.OrderByDescending(a => a.Severity).ToList(),
            settings,
            volumes,
            summary,
            collectors,
            volumesSupported);
    }

    public async Task<PerformanceDto> GetPerformanceAsync(int serverId, int hours, CancellationToken ct)
    {
        await EnsureVisibleAsync(serverId, ct);
        hours = Math.Clamp(hours, 1, 24 * 14);
        var from = clock.GetUtcNow().UtcDateTime.AddHours(-hours);

        // Bucket size keeps charts at a few hundred points whatever the range.
        int bucket = hours <= 3 ? 1 : hours <= 24 ? 5 : hours <= 72 ? 15 : 60;

        var rows = await db.ServerMetrics.AsNoTracking()
            .Where(m => m.ServerId == serverId && m.CollectedUtc >= from)
            .OrderBy(m => m.CollectedUtc)
            .ToListAsync(ct);

        var series = rows
            .GroupBy(m => Bucket(m.CollectedUtc, bucket))
            .Select(g => ToPoint(g.Key, g.ToList()))
            .ToList();

        var waitRows = await db.WaitStats.AsNoTracking()
            .Where(w => w.ServerId == serverId && w.CollectedUtc >= from)
            .GroupBy(w => w.WaitType)
            .Select(g => new
            {
                WaitType = g.Key,
                WaitTimeMs = g.Sum(x => x.WaitTimeMs),
                SignalWaitMs = g.Sum(x => x.SignalWaitMs),
                WaitingTasks = g.Sum(x => x.WaitingTasks),
            })
            .OrderByDescending(x => x.WaitTimeMs)
            .Take(15)
            .ToListAsync(ct);
        double totalWait = waitRows.Sum(w => (double)w.WaitTimeMs);
        var waits = waitRows.Select(w =>
        {
            var info = WaitTypeCatalog.Describe(w.WaitType);
            return new WaitRowDto(w.WaitType, info.Category, info.Explanation, w.WaitTimeMs, w.SignalWaitMs, w.WaitingTasks,
                w.WaitingTasks > 0 ? Math.Round((double)w.WaitTimeMs / w.WaitingTasks, 2) : 0,
                totalWait > 0 ? Math.Round(100.0 * w.WaitTimeMs / totalWait, 1) : 0);
        }).ToList();

        var fileRows = await db.FileIoStats.AsNoTracking()
            .Where(f => f.ServerId == serverId && f.CollectedUtc >= from)
            .GroupBy(f => new { f.DatabaseName, f.FileId })
            .Select(g => new
            {
                g.Key.DatabaseName,
                g.Key.FileId,
                Reads = g.Sum(x => x.Reads),
                Writes = g.Sum(x => x.Writes),
                ReadStall = g.Sum(x => x.ReadStallMs),
                WriteStall = g.Sum(x => x.WriteStallMs),
                BytesRead = g.Sum(x => x.BytesRead),
                BytesWritten = g.Sum(x => x.BytesWritten),
            })
            .ToListAsync(ct);
        var files = fileRows
            .Select(f => new FileLatencyDto(f.DatabaseName, f.FileId, f.Reads, f.Writes,
                f.Reads > 0 ? Math.Round((double)f.ReadStall / f.Reads, 2) : 0,
                f.Writes > 0 ? Math.Round((double)f.WriteStall / f.Writes, 2) : 0,
                Math.Round(f.BytesRead / 1048576.0, 1), Math.Round(f.BytesWritten / 1048576.0, 1)))
            .OrderByDescending(f => Math.Max(f.AvgReadMs, f.AvgWriteMs))
            .Take(20)
            .ToList();

        return new PerformanceDto(hours, bucket, series, waits, files);
    }

    public async Task<IReadOnlyList<DatabaseRowDto>> ListDatabasesAsync(int? serverId, bool includeDropped, CancellationToken ct)
    {
        var scope = await user.GetScopeAsync(ct);
        var servers = await db.Servers.AsNoTracking()
            .VisibleTo(scope)
            .Where(s => serverId == null || s.Id == serverId)
            .Include(s => s.Info)
            .Include(s => s.Capabilities)
            .ToListAsync(ct);
        if (serverId is not null && servers.Count == 0) throw new NotFoundException($"Server {serverId} was not found.");

        var ids = servers.Select(s => s.Id).ToList();
        var dbs = await db.Databases.AsNoTracking()
            .Include(d => d.Files)
            .Where(d => ids.Contains(d.ServerId) && (includeDropped || !d.IsDropped))
            .OrderBy(d => d.Name)
            .ToListAsync(ct);

        var byId = servers.ToDictionary(s => s.Id);
        var now = clock.GetUtcNow().UtcDateTime;
        return dbs.Select(d => ToRow(d, byId[d.ServerId], now).Row)
            .OrderBy(r => r.ServerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<DatabaseDetailDto> GetDatabaseAsync(int databaseId, CancellationToken ct)
    {
        var d = await db.Databases.AsNoTracking().Include(x => x.Files).FirstOrDefaultAsync(x => x.Id == databaseId, ct)
                ?? throw new NotFoundException($"Database {databaseId} was not found.");
        var scope = await user.GetScopeAsync(ct);
        var server = await db.Servers.AsNoTracking()
            .VisibleTo(scope)
            .Include(s => s.Info)
            .Include(s => s.Capabilities)
            .FirstOrDefaultAsync(s => s.Id == d.ServerId, ct)
            ?? throw new NotFoundException($"Database {databaseId} was not found.");

        var now = clock.GetUtcNow().UtcDateTime;
        var (row, advice) = ToRow(d, server, now);

        var since = now.AddDays(-90);
        var samples = await db.DatabaseSizeSamples.AsNoTracking()
            .Where(s => s.DatabaseId == databaseId && s.CollectedUtc >= since)
            .OrderBy(s => s.CollectedUtc)
            .ToListAsync(ct);
        // One point per day (the last sample of the day).
        var history = samples
            .GroupBy(s => s.CollectedUtc.Date)
            .Select(g => g.Last())
            .Select(s => new SizePointDto(s.CollectedUtc.Date, Math.Round(s.DataSizeMb, 1), Round(s.DataUsedMb), Math.Round(s.LogSizeMb, 1), Round(s.LogUsedMb)))
            .ToList();
        if (history.Count < 2)
        {
            // Not enough days yet: show the raw samples instead so the chart is not empty.
            history = samples.Select(s => new SizePointDto(s.CollectedUtc, Math.Round(s.DataSizeMb, 1), Round(s.DataUsedMb), Math.Round(s.LogSizeMb, 1), Round(s.LogUsedMb))).ToList();
        }

        var backups = (await db.BackupRecords.AsNoTracking()
                .Where(b => b.ServerId == d.ServerId && b.DatabaseName == d.Name)
                .OrderByDescending(b => b.FinishUtc)
                .Take(200)
                .ToListAsync(ct))
            .Select(b => new BackupRowDto(b.Id, b.BackupType, b.StartUtc, b.FinishUtc,
                (int)Math.Max(0, (b.FinishUtc - b.StartUtc).TotalSeconds), b.SizeBytes, b.CompressedBytes, b.IsCopyOnly, b.PhysicalDevice))
            .ToList();

        var files = d.Files.OrderBy(f => f.FileId)
            .Select(f => new DatabaseFileDto(f.FileId, f.LogicalName, f.PhysicalName, f.TypeDesc, Math.Round(f.SizeMb, 1), Round(f.UsedMb),
                Round(f.MaxSizeMb), f.IsPercentGrowth, f.GrowthValue, f.VolumeMountPoint))
            .ToList();

        return new DatabaseDetailDto(row, files, advice.OrderByDescending(a => a.Severity).ToList(), history, backups);
    }

    private (DatabaseRowDto Row, IReadOnlyList<AdviceItem> Advice) ToRow(Database d, Server s, DateTime now)
    {
        bool backupHistory = s.Capabilities.Any(c => c.CapabilityKey == CapabilityKeys.BackupHistory && c.IsSupported);
        var advice = DatabaseAdvisor.Evaluate(d, backupHistory, options.Value.Thresholds, now, HealthService.MaxCompatibilityLevel(s.Info));
        var row = new DatabaseRowDto(
            d.Id, s.Id, s.DisplayName, d.Name, d.State, d.RecoveryModel, d.CompatibilityLevel, d.Owner, d.IsReadOnly,
            Round(d.DataSizeMb), Round(d.DataUsedMb), Round(d.LogSizeMb), Round(d.LogUsedMb), d.VlfCount, d.LogReuseWait,
            d.LastFullBackupUtc, d.LastDiffBackupUtc, d.LastLogBackupUtc,
            d.LastGoodCheckDbUtc is { } c && c <= Database.NeverChecked ? null : d.LastGoodCheckDbUtc,
            d.LastGoodCheckDbUtc is { } n && n <= Database.NeverChecked,
            advice.Count(a => a.Severity == AdviceSeverity.Critical),
            advice.Count(a => a.Severity == AdviceSeverity.Warning),
            advice.Count(a => a.Severity == AdviceSeverity.Info),
            d.LastSeenUtc, d.IsDropped, backupHistory);
        return (row, advice);
    }

    private async Task EnsureVisibleAsync(int serverId, CancellationToken ct)
    {
        var scope = await user.GetScopeAsync(ct);
        if (!await db.Servers.VisibleTo(scope).AnyAsync(s => s.Id == serverId, ct))
        {
            throw new NotFoundException($"Server {serverId} was not found.");
        }
    }

    private static DateTime Bucket(DateTime t, int minutes)
    {
        var ticks = TimeSpan.FromMinutes(minutes).Ticks;
        return new DateTime(t.Ticks - (t.Ticks % ticks), DateTimeKind.Utc);
    }

    private static MetricPointDto ToPoint(DateTime time, IReadOnlyList<Domain.Monitoring.ServerMetric> m) => new(
        time,
        Avg(m.Select(x => x.SqlCpuPct)),
        Avg(m.Select(x => x.OtherCpuPct)),
        (long?)Avg(m.Select(x => (double?)x.PageLifeExpectancy)),
        m.Max(x => x.MemoryGrantsPending),
        (long?)Avg(m.Select(x => (double?)x.TotalServerMemoryMb)),
        (long?)Avg(m.Select(x => (double?)x.TargetServerMemoryMb)),
        Avg(m.Select(x => x.BatchRequestsPerSec)),
        (int?)Avg(m.Select(x => (double?)x.UserConnections)),
        m.Max(x => x.BlockedSessions));

    private static double? Avg(IEnumerable<double?> values)
    {
        var list = values.Where(v => v is not null).Select(v => v!.Value).ToList();
        return list.Count == 0 ? null : Math.Round(list.Average(), 2);
    }

    private static double? Round(double? v) => v is null ? null : Math.Round(v.Value, 1);
}
