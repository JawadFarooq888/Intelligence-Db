using IntelligenceDB.Domain.Servers;

namespace IntelligenceDB.Application.Monitoring;

public enum AdviceSeverity
{
    Info = 1,
    Warning = 2,
    Critical = 3,
}

/// <summary>One best-practice finding with a plain-English explanation and a recommended fix.</summary>
public sealed record AdviceItem(
    AdviceSeverity Severity,
    string Code,
    string Title,
    string Explanation,
    string? Current = null,
    string? Recommended = null,
    string? Target = null);

/// <summary>Optional: latest known build per major version (from settings) to flag outdated patch levels.</summary>
public sealed record LatestBuild(int Major, string Build, string Label);

/// <summary>Checks sp_configure settings, version and tempdb against common best practices.</summary>
public static class ConfigAdvisor
{
    private const long DefaultMaxServerMemory = 2147483647;

    public static IReadOnlyList<AdviceItem> Evaluate(
        IReadOnlyDictionary<string, long> config,
        ServerInfo? info,
        IReadOnlyList<DatabaseFile> tempdbFiles,
        LatestBuild? latestBuild)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(tempdbFiles);
        var items = new List<AdviceItem>();
        int cpus = info?.CpuCount ?? 0;
        long physicalMb = info?.PhysicalMemoryMb ?? 0;

        if (config.TryGetValue("max server memory (MB)", out var maxMem))
        {
            long recommended = physicalMb > 0 ? Math.Max(1024, physicalMb - Math.Max(4096, physicalMb / 10)) : 0;
            if (maxMem >= DefaultMaxServerMemory)
            {
                items.Add(new(AdviceSeverity.Warning, "CFG_MAXMEM_DEFAULT", "Max server memory is not set",
                    "SQL Server may take almost all memory and starve the operating system and other processes. Leave about 10% (at least 4 GB) for the OS.",
                    "unlimited (2147483647)", recommended > 0 ? $"{recommended} MB" : "physical memory minus 10% (min 4 GB)", "max server memory (MB)"));
            }
            else if (physicalMb > 0 && maxMem > physicalMb)
            {
                items.Add(new(AdviceSeverity.Warning, "CFG_MAXMEM_TOO_HIGH", "Max server memory is higher than physical memory",
                    "The limit is above the RAM of the machine, so it does not protect the operating system.",
                    $"{maxMem} MB", $"{recommended} MB", "max server memory (MB)"));
            }
        }

        if (config.TryGetValue("max degree of parallelism", out var maxdop) && maxdop == 0 && cpus > 8)
        {
            items.Add(new(AdviceSeverity.Warning, "CFG_MAXDOP", "MAXDOP is 0 on a server with more than 8 CPUs",
                "One query may use every CPU. Microsoft recommends at most 8 (or the number of cores per NUMA node).",
                "0", "8", "max degree of parallelism"));
        }

        if (config.TryGetValue("cost threshold for parallelism", out var ctfp) && ctfp == 5)
        {
            items.Add(new(AdviceSeverity.Warning, "CFG_CTFP", "Cost threshold for parallelism is the default (5)",
                "Even small queries go parallel, which wastes CPU and adds CXPACKET waits. A common starting point is 50.",
                "5", "50", "cost threshold for parallelism"));
        }

        if (config.TryGetValue("optimize for ad hoc workloads", out var adhoc) && adhoc == 0)
        {
            items.Add(new(AdviceSeverity.Info, "CFG_ADHOC", "Optimize for ad hoc workloads is off",
                "Single-use query plans fill the plan cache. Turning this on stores only a small stub until a query runs twice.",
                "0", "1", "optimize for ad hoc workloads"));
        }

        if (config.TryGetValue("backup compression default", out var bc) && bc == 0)
        {
            items.Add(new(AdviceSeverity.Info, "CFG_BACKUP_COMPRESSION", "Backup compression is off by default",
                "Compressed backups are usually much smaller and faster. Enable it unless you use a third-party tool that compresses.",
                "0", "1", "backup compression default"));
        }

        if (config.TryGetValue("remote admin connections", out var dac) && dac == 0)
        {
            items.Add(new(AdviceSeverity.Info, "CFG_REMOTE_DAC", "Remote dedicated admin connection is off",
                "The DAC lets an administrator connect even when the server is overloaded. Enabling it for remote use helps emergency troubleshooting.",
                "0", "1", "remote admin connections"));
        }

        if (config.TryGetValue("priority boost", out var pb) && pb == 1)
        {
            items.Add(new(AdviceSeverity.Critical, "CFG_PRIORITY_BOOST", "Priority boost is on",
                "This deprecated setting can starve the operating system and cause instability. Turn it off.",
                "1", "0", "priority boost"));
        }

        if (config.TryGetValue("lightweight pooling", out var lp) && lp == 1)
        {
            items.Add(new(AdviceSeverity.Warning, "CFG_FIBER_MODE", "Lightweight pooling (fiber mode) is on",
                "Fiber mode is rarely beneficial and breaks several features. Turn it off unless Microsoft support advised it.",
                "1", "0", "lightweight pooling"));
        }

        if (config.TryGetValue("xp_cmdshell", out var xp) && xp == 1)
        {
            items.Add(new(AdviceSeverity.Warning, "CFG_XP_CMDSHELL", "xp_cmdshell is enabled",
                "It lets T-SQL run operating-system commands - a common attack path. Disable it if it is not required.",
                "1", "0", "xp_cmdshell"));
        }

        var dataFiles = tempdbFiles.Where(f => f.TypeDesc == "ROWS").ToList();
        if (dataFiles.Count > 0 && cpus > 0)
        {
            int wanted = Math.Min(8, cpus);
            if (dataFiles.Count < wanted)
            {
                items.Add(new(AdviceSeverity.Warning, "TEMPDB_FILES", "tempdb has fewer data files than recommended",
                    "Too few tempdb data files cause allocation contention (PAGELATCH waits). Use one file per CPU up to 8.",
                    dataFiles.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    wanted.ToString(System.Globalization.CultureInfo.InvariantCulture), "tempdb"));
            }

            if (dataFiles.Select(f => Math.Round(f.SizeMb)).Distinct().Count() > 1)
            {
                items.Add(new(AdviceSeverity.Info, "TEMPDB_SIZES", "tempdb data files have different sizes",
                    "SQL Server fills files proportionally; unequal sizes make the biggest file a hot spot. Size all data files the same.",
                    null, "equal sizes and equal growth", "tempdb"));
            }
        }

        if (info is not null)
        {
            items.AddRange(VersionAdvice(info, latestBuild));
        }

        return items;
    }

    public static IEnumerable<AdviceItem> VersionAdvice(ServerInfo info, LatestBuild? latest)
    {
        ArgumentNullException.ThrowIfNull(info);
        bool boxProduct = info.EngineEdition is >= 1 and <= 4;
        if (!boxProduct) yield break;

        if (info.MajorVersion is > 0 and < 13)
        {
            yield return new(AdviceSeverity.Critical, "VER_UNSUPPORTED", "SQL Server version is out of support",
                "This version no longer gets security fixes from Microsoft. Plan an upgrade.", info.ProductVersion, "SQL Server 2019 or newer");
        }

        // Since SQL Server 2017 there are no service packs: RTM without a CU means no fixes are installed.
        if (info.MajorVersion >= 14 && string.Equals(info.ProductLevel, "RTM", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(info.ProductUpdateLevel))
        {
            yield return new(AdviceSeverity.Warning, "VER_NO_CU", "No cumulative update installed",
                "The server runs the original release without any cumulative update, so it misses many fixes, including security fixes.",
                info.ProductVersion, "latest cumulative update");
        }

        if (latest is not null && latest.Major == info.MajorVersion
            && Version.TryParse(info.ProductVersion, out var current) && Version.TryParse(latest.Build, out var newest)
            && current < newest)
        {
            yield return new(AdviceSeverity.Warning, "VER_OUTDATED_CU", "A newer cumulative update is available",
                $"Installed build {info.ProductVersion} is older than {latest.Label} ({latest.Build}).",
                info.ProductVersion, $"{latest.Label} ({latest.Build})");
        }
    }
}

/// <summary>Thresholds used by the database and health rules (configurable in appsettings "Monitoring").</summary>
public sealed class MonitoringThresholds
{
    public int FullBackupMaxAgeHours { get; set; } = 24 * 7;
    public int LogBackupMaxAgeHours { get; set; } = 24;
    public int CheckDbMaxAgeDays { get; set; } = 14;
    public int VlfWarning { get; set; } = 1000;
    public int VolumeFreeWarningPct { get; set; } = 20;
    public int VolumeFreeCriticalPct { get; set; } = 10;
    public double CpuWarningPct { get; set; } = 75;
    public double CpuCriticalPct { get; set; } = 90;
    public long PageLifeExpectancyWarning { get; set; } = 300;
}

/// <summary>Per-database checks: backups, CHECKDB, log reuse, VLFs, settings and file growth.</summary>
public static class DatabaseAdvisor
{
    public static IReadOnlyList<AdviceItem> Evaluate(
        Database db, bool backupHistoryAvailable, MonitoringThresholds t, DateTime nowUtc, int serverMaxCompatLevel)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(t);
        var items = new List<AdviceItem>();
        bool isTempdb = string.Equals(db.Name, "tempdb", StringComparison.OrdinalIgnoreCase);

        if (!string.Equals(db.State, "ONLINE", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(new(AdviceSeverity.Critical, "DB_STATE", $"Database is {db.State}",
                "The database is not online, so applications cannot use it.", db.State, "ONLINE", db.Name));
            return items;
        }

        if (backupHistoryAvailable && !isTempdb)
        {
            if (db.LastFullBackupUtc is null || nowUtc - db.LastFullBackupUtc.Value > TimeSpan.FromHours(t.FullBackupMaxAgeHours))
            {
                items.Add(new(AdviceSeverity.Critical, "DB_NO_FULL_BACKUP",
                    db.LastFullBackupUtc is null ? "No full backup found" : "Last full backup is too old",
                    "Without a recent full backup this database cannot be restored after a failure.",
                    db.LastFullBackupUtc?.ToString("u", System.Globalization.CultureInfo.InvariantCulture) ?? "never",
                    $"at least every {t.FullBackupMaxAgeHours / 24} days", db.Name));
            }

            bool needsLogBackups = db.RecoveryModel is "FULL" or "BULK_LOGGED";
            if (needsLogBackups && !db.IsReadOnly
                && (db.LastLogBackupUtc is null || nowUtc - db.LastLogBackupUtc.Value > TimeSpan.FromHours(t.LogBackupMaxAgeHours)))
            {
                items.Add(new(AdviceSeverity.Warning, "DB_NO_LOG_BACKUP", "Log backups missing in FULL recovery",
                    "In FULL recovery the transaction log keeps growing until it is backed up, and point-in-time restore needs log backups. Schedule log backups or switch to SIMPLE recovery.",
                    db.LastLogBackupUtc?.ToString("u", System.Globalization.CultureInfo.InvariantCulture) ?? "never",
                    $"at least every {t.LogBackupMaxAgeHours} h", db.Name));
            }
        }

        if (db.LastGoodCheckDbUtc is { } checkDb && !isTempdb)
        {
            bool never = checkDb <= Database.NeverChecked;
            if (never || nowUtc - checkDb > TimeSpan.FromDays(t.CheckDbMaxAgeDays))
            {
                items.Add(new(AdviceSeverity.Warning, "DB_CHECKDB", never ? "DBCC CHECKDB has never run" : "DBCC CHECKDB is overdue",
                    "CHECKDB finds corruption early, while good backups still exist. Run it regularly (e.g. weekly).",
                    never ? "never" : checkDb.ToString("u", System.Globalization.CultureInfo.InvariantCulture),
                    $"every {t.CheckDbMaxAgeDays} days or less", db.Name));
            }
        }

        if (!string.IsNullOrEmpty(db.LogReuseWait) && db.LogReuseWait is not ("NOTHING" or "CHECKPOINT"))
        {
            items.Add(new(AdviceSeverity.Info, "DB_LOG_REUSE_WAIT", $"Log space cannot be reused: {db.LogReuseWait}",
                LogReuseExplanation(db.LogReuseWait), db.LogReuseWait, "NOTHING", db.Name));
        }

        if (db.VlfCount is { } vlf && vlf > t.VlfWarning)
        {
            items.Add(new(AdviceSeverity.Warning, "DB_VLF", "Too many virtual log files (VLFs)",
                "Many small log growths create thousands of VLFs, which slows recovery, restores and log backups. Shrink the log once and regrow it in large steps.",
                vlf.ToString(System.Globalization.CultureInfo.InvariantCulture), $"under {t.VlfWarning}", db.Name));
        }

        if (db.IsAutoCloseOn)
        {
            items.Add(new(AdviceSeverity.Warning, "DB_AUTO_CLOSE", "AUTO_CLOSE is on",
                "The database shuts down after the last user leaves and must start again on the next connection, which is slow and flushes caches.",
                "ON", "OFF", db.Name));
        }

        if (db.IsAutoShrinkOn)
        {
            items.Add(new(AdviceSeverity.Warning, "DB_AUTO_SHRINK", "AUTO_SHRINK is on",
                "Automatic shrinking fragments indexes and wastes I/O because the files usually grow again.",
                "ON", "OFF", db.Name));
        }

        if (serverMaxCompatLevel > 0 && db.CompatibilityLevel > 0 && db.CompatibilityLevel < serverMaxCompatLevel - 20 && !IsSystem(db.Name))
        {
            items.Add(new(AdviceSeverity.Info, "DB_COMPAT", "Old compatibility level",
                "The database runs with an older query optimizer behaviour. Test and raise it to benefit from newer improvements.",
                db.CompatibilityLevel.ToString(System.Globalization.CultureInfo.InvariantCulture),
                serverMaxCompatLevel.ToString(System.Globalization.CultureInfo.InvariantCulture), db.Name));
        }

        foreach (var f in db.Files)
        {
            if (f.IsPercentGrowth)
            {
                items.Add(new(AdviceSeverity.Info, "FILE_PERCENT_GROWTH", $"File {f.LogicalName} grows by percent",
                    "Percentage growth makes every growth bigger and slower as the file grows. Use a fixed size such as 256 MB-1 GB.",
                    $"{f.GrowthValue:0}%", "fixed MB", db.Name));
            }
            else if (f.GrowthValue is > 0 and < 64)
            {
                items.Add(new(AdviceSeverity.Info, "FILE_SMALL_GROWTH", $"File {f.LogicalName} grows in tiny steps",
                    "Very small growth steps cause frequent pauses and, for logs, many VLFs.",
                    $"{f.GrowthValue:0.#} MB", "256 MB or more", db.Name));
            }

            if (f.MaxSizeMb is { } max && f.UsedMb is { } used && max > 0 && used / max >= 0.9)
            {
                items.Add(new(AdviceSeverity.Critical, "FILE_NEAR_MAX", $"File {f.LogicalName} is close to its maximum size",
                    "When the file reaches its max size, writes fail.",
                    $"{used:0} of {max:0} MB", "raise the max size or free space", db.Name));
            }
        }

        return items;
    }

    public static bool IsSystem(string name) =>
        name is "master" or "model" or "msdb" or "tempdb" or "distribution";

    private static string LogReuseExplanation(string wait) => wait switch
    {
        "LOG_BACKUP" => "The log is waiting for a log backup. Take log backups (FULL recovery), or the log keeps growing.",
        "ACTIVE_TRANSACTION" => "A long-running open transaction keeps the log from being reused. Find and end it.",
        "REPLICATION" => "Replication or CDC has not yet read the log records.",
        "AVAILABILITY_REPLICA" => "A secondary replica has not yet received or redone the log.",
        "DATABASE_MIRRORING" => "The mirror is behind.",
        "ACTIVE_BACKUP_OR_RESTORE" => "A backup or restore is running.",
        "OLDEST_PAGE" => "Indirect checkpoint has not yet written old dirty pages.",
        _ => "Something is holding on to log records; the log may grow until it is released.",
    };
}
