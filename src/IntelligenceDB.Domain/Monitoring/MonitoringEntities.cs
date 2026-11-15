namespace IntelligenceDB.Domain.Monitoring;

// History tables written by the collector. They are append-only and trimmed by the retention job.

/// <summary>One execution of one collector against one server (for diagnostics).</summary>
public class CollectionRun
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public string Collector { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTime StartedUtc { get; set; }
    public int DurationMs { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}

/// <summary>Server-level performance sample (every collection interval).</summary>
public class ServerMetric
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public DateTime CollectedUtc { get; set; }
    public double? SqlCpuPct { get; set; }
    public double? OtherCpuPct { get; set; }
    public long? PageLifeExpectancy { get; set; }
    public int? MemoryGrantsPending { get; set; }
    public long? TotalServerMemoryMb { get; set; }
    public long? TargetServerMemoryMb { get; set; }
    public double? BatchRequestsPerSec { get; set; }
    public int? UserConnections { get; set; }
    public int? BlockedSessions { get; set; }
}

/// <summary>Wait time accumulated during one interval (delta of sys.dm_os_wait_stats).</summary>
public class WaitStat
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public DateTime CollectedUtc { get; set; }
    public int IntervalSeconds { get; set; }
    public string WaitType { get; set; } = string.Empty;
    public long WaitTimeMs { get; set; }
    public long SignalWaitMs { get; set; }
    public long WaitingTasks { get; set; }
}

/// <summary>File I/O during one interval (delta of sys.dm_io_virtual_file_stats).</summary>
public class FileIoStat
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public DateTime CollectedUtc { get; set; }
    public int IntervalSeconds { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public int FileId { get; set; }
    public long Reads { get; set; }
    public long Writes { get; set; }
    public long BytesRead { get; set; }
    public long BytesWritten { get; set; }
    public long ReadStallMs { get; set; }
    public long WriteStallMs { get; set; }
}

public class VolumeStat
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public DateTime CollectedUtc { get; set; }
    public string MountPoint { get; set; } = string.Empty;
    public string? Label { get; set; }
    public long TotalMb { get; set; }
    public long FreeMb { get; set; }
}

/// <summary>Database size sample, for growth trends.</summary>
public class DatabaseSizeSample
{
    public long Id { get; set; }
    public int DatabaseId { get; set; }
    public DateTime CollectedUtc { get; set; }
    public double DataSizeMb { get; set; }
    public double? DataUsedMb { get; set; }
    public double LogSizeMb { get; set; }
    public double? LogUsedMb { get; set; }
}

/// <summary>One backup from msdb.dbo.backupset.</summary>
public class BackupRecord
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public long SourceBackupSetId { get; set; }

    /// <summary>D = full, I = differential, L = log, F = file/filegroup, G/P/Q = partial.</summary>
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
