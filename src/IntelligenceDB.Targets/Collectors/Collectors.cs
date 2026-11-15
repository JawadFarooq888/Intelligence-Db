using Dapper;
using IntelligenceDB.Application.Capabilities;
using IntelligenceDB.Application.Monitoring;
using IntelligenceDB.Domain.Servers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace IntelligenceDB.Targets.Collectors;

/// <summary>One kind of data collected from a server on its own schedule.</summary>
public interface ICollector
{
    string Name { get; }

    TimeSpan Interval(CollectionTarget target);

    /// <summary>False when the platform or the login's permissions do not allow this collector.</summary>
    bool IsSupported(CollectionTarget target);

    /// <summary>Fills <paramref name="batch"/>. SqlExceptions are handled by the engine (the batch is marked failed).</summary>
    Task CollectAsync(SqlConnection connection, CollectionTarget target, CollectorState state, CollectionBatch batch, CancellationToken ct);
}

/// <summary>
/// Per-server memory of the previous cumulative counters, so the collectors can send deltas
/// ("how much happened during the last interval"). Reset when the target changes or SQL restarts.
/// </summary>
public sealed class CollectorState
{
    internal Dictionary<string, (long Wait, long Signal, long Tasks)>? Waits { get; set; }
    internal DateTime WaitsAtUtc { get; set; }
    internal Dictionary<(int Db, int File), long[]>? Files { get; set; }
    internal DateTime FilesAtUtc { get; set; }
    internal long? BatchRequests { get; set; }
    internal DateTime BatchRequestsAtUtc { get; set; }
    internal long BackupWatermark { get; set; }
    internal bool VlfUnsupported { get; set; }
}

internal static class Cmd
{
    public const int TimeoutSeconds = 30;

    public static CommandDefinition Of(string sql, CancellationToken ct, object? parameters = null) =>
        new(sql, parameters, commandTimeout: TimeoutSeconds, cancellationToken: ct);

    public static bool IsAzureSqlDb(CollectionTarget t) => t.Platform == PlatformType.AzureSqlDatabase;
}

/// <summary>CPU, memory, batch requests, connections and blocked sessions.</summary>
public sealed class MetricsCollector(ILogger<MetricsCollector> logger) : ICollector
{
    public string Name => CollectorNames.Metrics;

    public TimeSpan Interval(CollectionTarget target) => TimeSpan.FromSeconds(Math.Max(15, target.MetricIntervalSeconds));

    public bool IsSupported(CollectionTarget target) => target.SupportedCapabilities.Contains(CapabilityKeys.ServerStateDmvs);

    public async Task CollectAsync(SqlConnection connection, CollectionTarget target, CollectorState state, CollectionBatch batch, CancellationToken ct)
    {
        var m = new MetricSample();

        try
        {
            if (Cmd.IsAzureSqlDb(target))
            {
                m.SqlCpuPct = await connection.ExecuteScalarAsync<double?>(Cmd.Of(CollectorQueries.CpuAzureSqlDb, ct));
            }
            else
            {
                var cpu = await connection.QueryFirstOrDefaultAsync<CpuRow>(Cmd.Of(CollectorQueries.CpuRingBuffer, ct));
                if (cpu?.SqlCpu is { } sql)
                {
                    m.SqlCpuPct = sql;
                    if (cpu.SystemIdle is { } idle) m.OtherCpuPct = Math.Max(0, 100 - idle - sql);
                }
            }
        }
        catch (SqlException ex)
        {
            // CPU is optional: some platforms hide the ring buffer.
            logger.LogDebug("CPU sample unavailable on server {ServerId}: {Message}", target.ServerId, ex.Message);
        }

        var counters = (await connection.QueryAsync<CounterRow>(Cmd.Of(CollectorQueries.PerfCounters, ct)))
            .GroupBy(c => c.CounterName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);

        if (counters.TryGetValue("Page life expectancy", out var ple)) m.PageLifeExpectancy = ple;
        if (counters.TryGetValue("Memory Grants Pending", out var grants)) m.MemoryGrantsPending = (int)grants;
        if (counters.TryGetValue("Total Server Memory (KB)", out var total)) m.TotalServerMemoryMb = total / 1024;
        if (counters.TryGetValue("Target Server Memory (KB)", out var target2)) m.TargetServerMemoryMb = target2 / 1024;
        if (counters.TryGetValue("User Connections", out var users)) m.UserConnections = (int)users;

        // "Batch Requests/sec" is really a cumulative counter: divide the change by the elapsed time.
        if (counters.TryGetValue("Batch Requests/sec", out var batches))
        {
            var now = batch.StartedUtc;
            if (state.BatchRequests is { } previous && batches >= previous && now > state.BatchRequestsAtUtc)
            {
                m.BatchRequestsPerSec = Math.Round((batches - previous) / (now - state.BatchRequestsAtUtc).TotalSeconds, 2);
            }

            state.BatchRequests = batches;
            state.BatchRequestsAtUtc = now;
        }

        m.BlockedSessions = await connection.ExecuteScalarAsync<int>(Cmd.Of(CollectorQueries.BlockedSessions, ct));
        batch.Metric = m;
    }

    internal sealed class CpuRow
    {
        public int? SqlCpu { get; set; }
        public int? SystemIdle { get; set; }
    }

    internal sealed class CounterRow
    {
        public string CounterName { get; set; } = string.Empty;
        public long Value { get; set; }
    }
}

/// <summary>Wait statistics as deltas between two samples.</summary>
public sealed class WaitsCollector : ICollector
{
    public string Name => CollectorNames.Waits;

    public TimeSpan Interval(CollectionTarget target) => TimeSpan.FromMinutes(5);

    public bool IsSupported(CollectionTarget target) => target.SupportedCapabilities.Contains(CapabilityKeys.ServerStateDmvs);

    public async Task CollectAsync(SqlConnection connection, CollectionTarget target, CollectorState state, CollectionBatch batch, CancellationToken ct)
    {
        var sql = Cmd.IsAzureSqlDb(target) ? CollectorQueries.WaitStatsAzureSqlDb : CollectorQueries.WaitStats;
        var rows = (await connection.QueryAsync<WaitRow>(Cmd.Of(sql, ct))).ToList();
        var current = rows.GroupBy(r => r.WaitType)
            .ToDictionary(g => g.Key, g => (g.Sum(x => x.WaitTimeMs), g.Sum(x => x.SignalWaitMs), g.Sum(x => x.WaitingTasks)));

        var result = new List<WaitSample>();
        var previous = state.Waits;
        int seconds = (int)Math.Round((batch.StartedUtc - state.WaitsAtUtc).TotalSeconds);

        // First sample only sets the baseline. Counters going down means SQL restarted or stats were cleared.
        bool restarted = previous is not null && current.Any(c => previous.TryGetValue(c.Key, out var p) && c.Value.Item1 < p.Wait);
        if (previous is not null && !restarted && seconds > 0)
        {
            foreach (var (type, (wait, signal, tasks)) in current)
            {
                previous.TryGetValue(type, out var p);
                long dWait = wait - p.Wait;
                if (dWait <= 0) continue;
                result.Add(new WaitSample(type, dWait, Math.Max(0, signal - p.Signal), Math.Max(0, tasks - p.Tasks), seconds));
            }
        }

        state.Waits = current;
        state.WaitsAtUtc = batch.StartedUtc;
        batch.Waits = result;
    }

    internal sealed class WaitRow
    {
        public string WaitType { get; set; } = string.Empty;
        public long WaitTimeMs { get; set; }
        public long SignalWaitMs { get; set; }
        public long WaitingTasks { get; set; }
    }
}

/// <summary>Per-file I/O (reads, writes, stall time) as deltas, for latency per file.</summary>
public sealed class FileIoCollector : ICollector
{
    public string Name => CollectorNames.FileIo;

    public TimeSpan Interval(CollectionTarget target) => TimeSpan.FromMinutes(5);

    public bool IsSupported(CollectionTarget target) => target.SupportedCapabilities.Contains(CapabilityKeys.ServerStateDmvs);

    public async Task CollectAsync(SqlConnection connection, CollectionTarget target, CollectorState state, CollectionBatch batch, CancellationToken ct)
    {
        var sql = Cmd.IsAzureSqlDb(target) ? CollectorQueries.FileStatsAzureSqlDb : CollectorQueries.FileStats;
        var rows = (await connection.QueryAsync<FileRow>(Cmd.Of(sql, ct))).ToList();
        var current = rows.ToDictionary(r => (r.DatabaseId, r.FileId),
            r => new[] { r.Reads, r.Writes, r.BytesRead, r.BytesWritten, r.ReadStallMs, r.WriteStallMs });
        var names = rows.ToDictionary(r => (r.DatabaseId, r.FileId), r => r.DatabaseName ?? $"db{r.DatabaseId}");

        var result = new List<FileIoSample>();
        var previous = state.Files;
        int seconds = (int)Math.Round((batch.StartedUtc - state.FilesAtUtc).TotalSeconds);
        if (previous is not null && seconds > 0)
        {
            foreach (var (key, now) in current)
            {
                if (!previous.TryGetValue(key, out var p)) continue;
                var d = now.Zip(p, (a, b) => a - b).ToArray();
                if (d.Any(x => x < 0)) continue; // file recreated or restart
                if (d[0] + d[1] == 0) continue;
                result.Add(new FileIoSample(names[key], key.Item2, d[0], d[1], d[2], d[3], d[4], d[5], seconds));
            }
        }

        state.Files = current;
        state.FilesAtUtc = batch.StartedUtc;
        batch.FileIo = result;
    }

    internal sealed class FileRow
    {
        public string? DatabaseName { get; set; }
        public int DatabaseId { get; set; }
        public int FileId { get; set; }
        public long Reads { get; set; }
        public long Writes { get; set; }
        public long BytesRead { get; set; }
        public long BytesWritten { get; set; }
        public long ReadStallMs { get; set; }
        public long WriteStallMs { get; set; }
    }
}

/// <summary>Databases, files, used space, VLFs, CHECKDB date and volumes.</summary>
public sealed class DatabasesCollector(ILogger<DatabasesCollector> logger) : ICollector
{
    /// <summary>Above this many databases, per-database queries (used space, VLFs) are skipped to stay light.</summary>
    private const int MaxPerDatabaseQueries = 300;

    public string Name => CollectorNames.Databases;

    public TimeSpan Interval(CollectionTarget target) => TimeSpan.FromMinutes(15);

    public bool IsSupported(CollectionTarget target) => true;

    public async Task CollectAsync(SqlConnection connection, CollectionTarget target, CollectorState state, CollectionBatch batch, CancellationToken ct)
    {
        bool sqlDb = Cmd.IsAzureSqlDb(target);
        var dbRows = (await connection.QueryAsync<DbRow>(Cmd.Of(sqlDb ? CollectorQueries.DatabasesAzureSqlDb : CollectorQueries.Databases, ct))).ToList();
        var files = (await connection.QueryAsync<DatabaseFileSample>(Cmd.Of(sqlDb ? CollectorQueries.FilesAzureSqlDb : CollectorQueries.Files, ct))).ToList();

        // Volumes (where allowed) and the volume of each file.
        if (!sqlDb && target.SupportedCapabilities.Contains(CapabilityKeys.VolumeStats))
        {
            try
            {
                var vols = (await connection.QueryAsync<VolumeRow>(Cmd.Of(CollectorQueries.Volumes, ct))).ToList();
                var byFile = vols.ToDictionary(v => (v.DatabaseId, v.FileId), v => v.MountPoint);
                foreach (var f in files)
                {
                    if (byFile.TryGetValue((f.DatabaseId, f.FileId), out var mp)) f.VolumeMountPoint = mp;
                }

                batch.Volumes = vols.GroupBy(v => v.MountPoint, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .Select(v => new VolumeSample(v.MountPoint, v.Label, v.TotalMb, v.FreeMb))
                    .ToList();
            }
            catch (SqlException ex)
            {
                logger.LogDebug("Volume stats unavailable on server {ServerId}: {Message}", target.ServerId, ex.Message);
            }
        }

        var samples = dbRows.Select(r => new DatabaseSample
        {
            DatabaseId = r.DatabaseId,
            Name = r.Name,
            CreateDateUtc = r.CreateDateUtc is { } c ? DateTime.SpecifyKind(c, DateTimeKind.Utc) : null,
            State = r.State,
            RecoveryModel = r.RecoveryModel,
            CompatibilityLevel = r.CompatibilityLevel,
            Owner = r.Owner,
            IsReadOnly = r.IsReadOnly,
            IsAutoCloseOn = r.IsAutoCloseOn,
            IsAutoShrinkOn = r.IsAutoShrinkOn,
            LogReuseWait = r.LogReuseWait,
            LastGoodCheckDbUtc = CheckDbUtc(r.LastGoodCheckDbLocal, r.UtcOffsetSeconds),
        }).ToList();

        // Used space and VLFs need to run inside each database.
        var original = connection.Database;
        var accessible = dbRows.Where(r => r.HasAccess && r.State == "ONLINE").Take(MaxPerDatabaseQueries).ToList();
        foreach (var r in accessible)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!sqlDb && !string.Equals(connection.Database, r.Name, StringComparison.Ordinal))
                {
                    await connection.ChangeDatabaseAsync(r.Name, ct);
                }

                var used = (await connection.QueryAsync<UsedRow>(Cmd.Of(CollectorQueries.UsedSpace, ct))).ToDictionary(u => u.FileId, u => u.UsedMb);
                foreach (var f in files.Where(f => f.DatabaseId == r.DatabaseId))
                {
                    if (used.TryGetValue(f.FileId, out var mb)) f.UsedMb = mb;
                }

                if (!state.VlfUnsupported)
                {
                    try
                    {
                        samples.First(s => s.DatabaseId == r.DatabaseId).VlfCount =
                            await connection.ExecuteScalarAsync<int>(Cmd.Of(CollectorQueries.VlfCount, ct));
                    }
                    catch (SqlException ex) when (ex.Number is 208 or 4121 or 195)
                    {
                        // sys.dm_db_log_info does not exist on this version: stop asking.
                        state.VlfUnsupported = true;
                    }
                }
            }
            catch (SqlException ex)
            {
                logger.LogDebug("Skipping per-database details of {Database} on server {ServerId}: {Message}", r.Name, target.ServerId, ex.Message);
            }
        }

        if (!sqlDb && !string.Equals(connection.Database, original, StringComparison.Ordinal))
        {
            await connection.ChangeDatabaseAsync(original, ct);
        }

        batch.Databases = new DatabaseSnapshot { Databases = samples, Files = files };
    }

    /// <summary>LastGoodCheckDbTime is server-local; 1900-01-01 means "never".</summary>
    internal static DateTime? CheckDbUtc(DateTime? local, int offsetSeconds)
    {
        if (local is null) return null;
        if (local.Value.Year <= 1900) return Database.NeverChecked;
        return DateTime.SpecifyKind(local.Value.AddSeconds(offsetSeconds), DateTimeKind.Utc);
    }

    internal sealed class DbRow
    {
        public int DatabaseId { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime? CreateDateUtc { get; set; }
        public string State { get; set; } = string.Empty;
        public string RecoveryModel { get; set; } = string.Empty;
        public int CompatibilityLevel { get; set; }
        public string? Owner { get; set; }
        public bool IsReadOnly { get; set; }
        public bool IsAutoCloseOn { get; set; }
        public bool IsAutoShrinkOn { get; set; }
        public string? LogReuseWait { get; set; }
        public bool HasAccess { get; set; }
        public DateTime? LastGoodCheckDbLocal { get; set; }
        public int UtcOffsetSeconds { get; set; }
    }

    internal sealed class VolumeRow
    {
        public int DatabaseId { get; set; }
        public int FileId { get; set; }
        public string MountPoint { get; set; } = string.Empty;
        public string? Label { get; set; }
        public long TotalMb { get; set; }
        public long FreeMb { get; set; }
    }

    internal sealed class UsedRow
    {
        public int FileId { get; set; }
        public double? UsedMb { get; set; }
    }
}

/// <summary>Last backup per database and type, plus new backup history rows from msdb.</summary>
public sealed class BackupsCollector : ICollector
{
    /// <summary>On the first run after start, history of this many days is loaded (duplicates are ignored on ingest).</summary>
    private const int InitialDays = 14;

    public string Name => CollectorNames.Backups;

    public TimeSpan Interval(CollectionTarget target) => TimeSpan.FromMinutes(15);

    public bool IsSupported(CollectionTarget target) => target.SupportedCapabilities.Contains(CapabilityKeys.BackupHistory);

    public async Task CollectAsync(SqlConnection connection, CollectionTarget target, CollectorState state, CollectionBatch batch, CancellationToken ct)
    {
        var latest = (await connection.QueryAsync<LatestRow>(Cmd.Of(CollectorQueries.LatestBackups, ct)))
            .Select(l => new LatestBackupSample(l.DatabaseName, l.BackupType, DateTime.SpecifyKind(l.LastFinishUtc, DateTimeKind.Utc)))
            .ToList();

        var history = (await connection.QueryAsync<HistoryRow>(Cmd.Of(CollectorQueries.BackupHistory, ct,
                new { after = state.BackupWatermark, days = InitialDays })))
            .Select(h => new BackupSample(h.BackupSetId, h.DatabaseName, h.BackupType,
                DateTime.SpecifyKind(h.StartUtc, DateTimeKind.Utc), DateTime.SpecifyKind(h.FinishUtc, DateTimeKind.Utc),
                h.SizeBytes, h.CompressedBytes, h.IsCopyOnly, h.RecoveryModel, h.DeviceType, h.PhysicalDevice))
            .ToList();

        if (history.Count > 0) state.BackupWatermark = history.Max(h => h.BackupSetId);
        batch.Backups = new BackupSnapshot { Latest = latest, History = history };
    }

    internal sealed class LatestRow
    {
        public string DatabaseName { get; set; } = string.Empty;
        public string BackupType { get; set; } = "D";
        public DateTime LastFinishUtc { get; set; }
    }

    internal sealed class HistoryRow
    {
        public long BackupSetId { get; set; }
        public string DatabaseName { get; set; } = string.Empty;
        public string BackupType { get; set; } = "D";
        public DateTime StartUtc { get; set; }
        public DateTime FinishUtc { get; set; }
        public long SizeBytes { get; set; }
        public long? CompressedBytes { get; set; }
        public bool IsCopyOnly { get; set; }
        public string? RecoveryModel { get; set; }
        public int? DeviceType { get; set; }
        public string? PhysicalDevice { get; set; }
    }
}

/// <summary>sp_configure settings (not available on Azure SQL Database).</summary>
public sealed class ConfigCollector : ICollector
{
    public string Name => CollectorNames.Config;

    public TimeSpan Interval(CollectionTarget target) => TimeSpan.FromHours(6);

    public bool IsSupported(CollectionTarget target) => !Cmd.IsAzureSqlDb(target);

    public async Task CollectAsync(SqlConnection connection, CollectionTarget target, CollectorState state, CollectionBatch batch, CancellationToken ct)
    {
        batch.Config = (await connection.QueryAsync<ConfigRow>(Cmd.Of(CollectorQueries.Configuration, ct)))
            .Select(c => new ConfigSample(c.Name, c.Value, c.ValueInUse, c.Minimum, c.Maximum, c.IsDynamic, c.IsAdvanced, c.Description))
            .ToList();
    }

    internal sealed class ConfigRow
    {
        public string Name { get; set; } = string.Empty;
        public long Value { get; set; }
        public long ValueInUse { get; set; }
        public long Minimum { get; set; }
        public long Maximum { get; set; }
        public bool IsDynamic { get; set; }
        public bool IsAdvanced { get; set; }
        public string? Description { get; set; }
    }
}
