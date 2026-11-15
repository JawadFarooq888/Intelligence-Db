namespace IntelligenceDB.Domain.Servers;

/// <summary>Current state of one database on a monitored server (updated by the collector).</summary>
public class Database
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SourceDatabaseId { get; set; }
    public DateTime? CreateDateUtc { get; set; }
    public string State { get; set; } = string.Empty;
    public string RecoveryModel { get; set; } = string.Empty;
    public int CompatibilityLevel { get; set; }
    public string? Owner { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsAutoCloseOn { get; set; }
    public bool IsAutoShrinkOn { get; set; }
    public string? LogReuseWait { get; set; }
    public int? VlfCount { get; set; }

    /// <summary>
    /// Last successful DBCC CHECKDB. Null = the server does not report it (needs SQL 2016 SP2+);
    /// <see cref="NeverChecked"/> = it was never run.
    /// </summary>
    public DateTime? LastGoodCheckDbUtc { get; set; }

    public DateTime? LastFullBackupUtc { get; set; }
    public DateTime? LastDiffBackupUtc { get; set; }
    public DateTime? LastLogBackupUtc { get; set; }

    public double? DataSizeMb { get; set; }
    public double? DataUsedMb { get; set; }
    public double? LogSizeMb { get; set; }
    public double? LogUsedMb { get; set; }

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }

    /// <summary>The database no longer exists on the server (kept for history).</summary>
    public bool IsDropped { get; set; }

    public List<DatabaseFile> Files { get; set; } = [];

    /// <summary>SQL Server reports 1900-01-01 when CHECKDB never ran.</summary>
    public static readonly DateTime NeverChecked = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}

public class DatabaseFile
{
    public int DatabaseId { get; set; }
    public int FileId { get; set; }
    public string LogicalName { get; set; } = string.Empty;
    public string? PhysicalName { get; set; }

    /// <summary>ROWS, LOG, FILESTREAM, FULLTEXT.</summary>
    public string TypeDesc { get; set; } = string.Empty;

    public double SizeMb { get; set; }
    public double? UsedMb { get; set; }

    /// <summary>Null = unlimited.</summary>
    public double? MaxSizeMb { get; set; }

    public bool IsPercentGrowth { get; set; }

    /// <summary>Percent when <see cref="IsPercentGrowth"/>, otherwise MB.</summary>
    public double GrowthValue { get; set; }

    public string? VolumeMountPoint { get; set; }
}

/// <summary>One sp_configure setting (latest snapshot).</summary>
public class ServerConfigSetting
{
    public int ServerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long Value { get; set; }
    public long ValueInUse { get; set; }
    public long Minimum { get; set; }
    public long Maximum { get; set; }
    public bool IsDynamic { get; set; }
    public bool IsAdvanced { get; set; }
    public string? Description { get; set; }
    public DateTime CollectedUtc { get; set; }
}

/// <summary>Latest 0-100 health score and the reasons for every lost point (JSON).</summary>
public class ServerHealth
{
    public int ServerId { get; set; }
    public int Score { get; set; }
    public string BreakdownJson { get; set; } = "[]";
    public DateTime CalculatedUtc { get; set; }
}
