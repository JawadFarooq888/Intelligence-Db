using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Application.Servers;
using IntelligenceDB.Domain.Monitoring;
using IntelligenceDB.Domain.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IntelligenceDB.Application.Monitoring;

/// <summary>
/// Stores collection results in the repository. Called by the central Worker (direct servers)
/// and by the API for batches sent in by agents. Every server is processed independently so one
/// bad batch never blocks the others.
/// </summary>
public sealed class IngestionService(
    IRepoDbContext db,
    HealthService health,
    ILogger<IngestionService> logger)
{
    /// <param name="source">"worker" or "agent:&lt;name&gt;" (shown in diagnostics).</param>
    /// <param name="agentId">For agent batches: only servers assigned to this agent are accepted.</param>
    public async Task<int> IngestAsync(IReadOnlyList<CollectionBatch> batches, string source, Guid? agentId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batches);
        int accepted = 0;

        foreach (var group in batches.GroupBy(b => b.ServerId))
        {
            var server = await db.Servers
                .Include(s => s.Info)
                .Include(s => s.Capabilities)
                .FirstOrDefaultAsync(s => s.Id == group.Key, ct);

            if (server is null || server.AgentId != agentId)
            {
                // A server that was deleted or moved to another agent/the portal: ignore its data.
                logger.LogWarning("Ignoring {Count} batch(es) for server {ServerId} from {Source}: not assigned to this collector",
                    group.Count(), group.Key, source);
                continue;
            }

            foreach (var batch in group.OrderBy(b => b.StartedUtc))
            {
                await ApplyAsync(server, batch, source, ct);
                accepted++;
            }

            await db.SaveChangesAsync(ct);
            await AddSizeSamplesAsync(server.Id, group, ct);
            await health.RecalculateAsync(server.Id, ct);
        }

        return accepted;
    }

    private async Task ApplyAsync(Server server, CollectionBatch batch, string source, CancellationToken ct)
    {
        var at = batch.StartedUtc;
        db.CollectionRuns.Add(new CollectionRun
        {
            ServerId = server.Id,
            Collector = Truncate(batch.Collector, 32)!,
            Source = Truncate(source, 128)!,
            StartedUtc = at,
            DurationMs = batch.DurationMs,
            Success = batch.Success,
            Error = Truncate(batch.Error, 2000),
        });

        if (batch.Probe is not null)
        {
            ProbeResultApplier.Apply(server, batch.Probe, at);
        }
        else if (!batch.Success && batch.ConnectionStatus is { } failed)
        {
            server.LastStatus = failed;
            server.LastError = Truncate(batch.Error, 2000);
            server.LastCheckedUtc = at;
        }
        else if (batch.Success && (server.LastCheckedUtc is null || at >= server.LastCheckedUtc))
        {
            server.LastStatus = ConnectionStatus.Online;
            server.LastError = null;
            server.LastCheckedUtc = at;
        }

        if (!batch.Success) return;

        if (batch.Metric is { } m)
        {
            db.ServerMetrics.Add(new ServerMetric
            {
                ServerId = server.Id,
                CollectedUtc = at,
                SqlCpuPct = m.SqlCpuPct,
                OtherCpuPct = m.OtherCpuPct,
                PageLifeExpectancy = m.PageLifeExpectancy,
                MemoryGrantsPending = m.MemoryGrantsPending,
                TotalServerMemoryMb = m.TotalServerMemoryMb,
                TargetServerMemoryMb = m.TargetServerMemoryMb,
                BatchRequestsPerSec = m.BatchRequestsPerSec,
                UserConnections = m.UserConnections,
                BlockedSessions = m.BlockedSessions,
            });
        }

        if (batch.Waits is { Count: > 0 } waits)
        {
            db.WaitStats.AddRange(waits
                .Where(w => w.WaitTimeMs > 0 && !WaitTypeCatalog.IsBenign(w.WaitType))
                .Select(w => new WaitStat
                {
                    ServerId = server.Id,
                    CollectedUtc = at,
                    IntervalSeconds = w.IntervalSeconds,
                    WaitType = Truncate(w.WaitType, 120)!,
                    WaitTimeMs = w.WaitTimeMs,
                    SignalWaitMs = w.SignalWaitMs,
                    WaitingTasks = w.WaitingTasks,
                }));
        }

        if (batch.FileIo is { Count: > 0 } io)
        {
            db.FileIoStats.AddRange(io
                .Where(f => f.Reads + f.Writes > 0)
                .Select(f => new FileIoStat
                {
                    ServerId = server.Id,
                    CollectedUtc = at,
                    IntervalSeconds = f.IntervalSeconds,
                    DatabaseName = Truncate(f.DatabaseName, 128) ?? "?",
                    FileId = f.FileId,
                    Reads = f.Reads,
                    Writes = f.Writes,
                    BytesRead = f.BytesRead,
                    BytesWritten = f.BytesWritten,
                    ReadStallMs = f.ReadStallMs,
                    WriteStallMs = f.WriteStallMs,
                }));
        }

        if (batch.Volumes is { Count: > 0 } volumes)
        {
            db.VolumeStats.AddRange(volumes.Select(v => new VolumeStat
            {
                ServerId = server.Id,
                CollectedUtc = at,
                MountPoint = Truncate(v.MountPoint, 260)!,
                Label = Truncate(v.Label, 128),
                TotalMb = v.TotalMb,
                FreeMb = v.FreeMb,
            }));
        }

        if (batch.Config is { Count: > 0 } config)
        {
            await ApplyConfigAsync(server.Id, config, at, ct);
        }

        if (batch.Databases is { } snapshot)
        {
            await ApplyDatabasesAsync(server.Id, snapshot, at, ct);
        }

        if (batch.Backups is { } backups)
        {
            await ApplyBackupsAsync(server.Id, backups, ct);
        }
    }

    private async Task ApplyConfigAsync(int serverId, List<ConfigSample> config, DateTime at, CancellationToken ct)
    {
        var existing = (await db.ServerConfigSettings.Where(c => c.ServerId == serverId).ToListAsync(ct))
            .ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var c in config.DistinctBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!existing.Remove(c.Name, out var row))
            {
                row = new ServerConfigSetting { ServerId = serverId, Name = Truncate(c.Name, 128)! };
                db.ServerConfigSettings.Add(row);
            }

            row.Value = c.Value;
            row.ValueInUse = c.ValueInUse;
            row.Minimum = c.Minimum;
            row.Maximum = c.Maximum;
            row.IsDynamic = c.IsDynamic;
            row.IsAdvanced = c.IsAdvanced;
            row.Description = Truncate(c.Description, 512);
            row.CollectedUtc = at;
        }

        db.ServerConfigSettings.RemoveRange(existing.Values);
    }

    private async Task ApplyDatabasesAsync(int serverId, DatabaseSnapshot snapshot, DateTime at, CancellationToken ct)
    {
        var existing = await db.Databases.Include(d => d.Files).Where(d => d.ServerId == serverId).ToListAsync(ct);
        var byName = existing.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in snapshot.Databases)
        {
            seen.Add(s.Name);
            if (!byName.TryGetValue(s.Name, out var d))
            {
                d = new Database { ServerId = serverId, Name = Truncate(s.Name, 128)!, FirstSeenUtc = at };
                db.Databases.Add(d);
                byName[s.Name] = d;
            }

            d.SourceDatabaseId = s.DatabaseId;
            d.CreateDateUtc = s.CreateDateUtc;
            d.State = Truncate(s.State, 60) ?? string.Empty;
            d.RecoveryModel = Truncate(s.RecoveryModel, 60) ?? string.Empty;
            d.CompatibilityLevel = s.CompatibilityLevel;
            d.Owner = Truncate(s.Owner, 128);
            d.IsReadOnly = s.IsReadOnly;
            d.IsAutoCloseOn = s.IsAutoCloseOn;
            d.IsAutoShrinkOn = s.IsAutoShrinkOn;
            d.LogReuseWait = Truncate(s.LogReuseWait, 60);
            d.VlfCount = s.VlfCount;
            d.LastGoodCheckDbUtc = s.LastGoodCheckDbUtc;
            d.LastSeenUtc = at;
            d.IsDropped = false;

            var files = snapshot.Files.Where(f => f.DatabaseId == s.DatabaseId).ToList();
            var fileById = d.Files.ToDictionary(f => f.FileId);
            foreach (var f in files)
            {
                if (!fileById.Remove(f.FileId, out var row))
                {
                    row = new DatabaseFile { FileId = f.FileId };
                    d.Files.Add(row);
                }

                row.LogicalName = Truncate(f.LogicalName, 128) ?? string.Empty;
                row.PhysicalName = Truncate(f.PhysicalName, 260);
                row.TypeDesc = Truncate(f.TypeDesc, 60) ?? string.Empty;
                row.SizeMb = f.SizeMb;
                row.UsedMb = f.UsedMb;
                row.MaxSizeMb = f.MaxSizeMb;
                row.IsPercentGrowth = f.IsPercentGrowth;
                row.GrowthValue = f.GrowthValue;
                row.VolumeMountPoint = Truncate(f.VolumeMountPoint, 260);
            }

            foreach (var gone in fileById.Values) d.Files.Remove(gone);

            var data = d.Files.Where(f => f.TypeDesc == "ROWS").ToList();
            var log = d.Files.Where(f => f.TypeDesc == "LOG").ToList();
            d.DataSizeMb = data.Count == 0 ? null : data.Sum(f => f.SizeMb);
            d.DataUsedMb = data.Count == 0 || data.Any(f => f.UsedMb is null) ? null : data.Sum(f => f.UsedMb!.Value);
            d.LogSizeMb = log.Count == 0 ? null : log.Sum(f => f.SizeMb);
            d.LogUsedMb = log.Count == 0 || log.Any(f => f.UsedMb is null) ? null : log.Sum(f => f.UsedMb!.Value);
        }

        foreach (var d in existing.Where(d => !seen.Contains(d.Name) && !d.IsDropped))
        {
            d.IsDropped = true;
        }
    }

    private async Task ApplyBackupsAsync(int serverId, BackupSnapshot snapshot, CancellationToken ct)
    {
        var dbs = await db.Databases.Where(d => d.ServerId == serverId).ToListAsync(ct);
        foreach (var d in dbs)
        {
            DateTime? Last(string type) => snapshot.Latest
                .Where(l => l.BackupType == type && string.Equals(l.DatabaseName, d.Name, StringComparison.OrdinalIgnoreCase))
                .Select(l => (DateTime?)l.LastFinishUtc)
                .Max();

            d.LastFullBackupUtc = Last("D");
            d.LastDiffBackupUtc = Last("I");
            d.LastLogBackupUtc = Last("L");
        }

        if (snapshot.History.Count == 0) return;

        var ids = snapshot.History.Select(h => h.BackupSetId).Distinct().ToList();
        var known = (await db.BackupRecords
                .Where(b => b.ServerId == serverId && ids.Contains(b.SourceBackupSetId))
                .Select(b => b.SourceBackupSetId)
                .ToListAsync(ct))
            .ToHashSet();

        db.BackupRecords.AddRange(snapshot.History
            .Where(h => known.Add(h.BackupSetId))
            .Select(h => new BackupRecord
            {
                ServerId = serverId,
                SourceBackupSetId = h.BackupSetId,
                DatabaseName = Truncate(h.DatabaseName, 128) ?? "?",
                BackupType = Truncate(h.BackupType, 1) ?? "D",
                StartUtc = h.StartUtc,
                FinishUtc = h.FinishUtc,
                SizeBytes = h.SizeBytes,
                CompressedBytes = h.CompressedBytes,
                IsCopyOnly = h.IsCopyOnly,
                RecoveryModel = Truncate(h.RecoveryModel, 60),
                DeviceType = h.DeviceType,
                PhysicalDevice = Truncate(h.PhysicalDevice, 400),
            }));
    }

    /// <summary>Growth history: one size sample per database per database collection.</summary>
    private async Task AddSizeSamplesAsync(int serverId, IEnumerable<CollectionBatch> batches, CancellationToken ct)
    {
        var dbBatch = batches.LastOrDefault(b => b.Success && b.Databases is not null);
        if (dbBatch is null) return;

        var dbs = await db.Databases.AsNoTracking()
            .Where(d => d.ServerId == serverId && !d.IsDropped && d.DataSizeMb != null)
            .Select(d => new { d.Id, d.DataSizeMb, d.DataUsedMb, d.LogSizeMb, d.LogUsedMb })
            .ToListAsync(ct);

        db.DatabaseSizeSamples.AddRange(dbs.Select(d => new DatabaseSizeSample
        {
            DatabaseId = d.Id,
            CollectedUtc = dbBatch.StartedUtc,
            DataSizeMb = d.DataSizeMb ?? 0,
            DataUsedMb = d.DataUsedMb,
            LogSizeMb = d.LogSizeMb ?? 0,
            LogUsedMb = d.LogUsedMb,
        }));
        await db.SaveChangesAsync(ct);
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
