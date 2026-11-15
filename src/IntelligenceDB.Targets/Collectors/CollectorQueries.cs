namespace IntelligenceDB.Targets.Collectors;

/// <summary>
/// T-SQL used by the collectors. All queries read only system metadata / DMVs, return small results,
/// and run on a session with LOCK_TIMEOUT 3000, DEADLOCK_PRIORITY LOW and READ UNCOMMITTED
/// (see <see cref="SqlTargetConnectionFactory"/>). Local server times are converted to UTC in SQL.
/// </summary>
internal static class CollectorQueries
{
    // ---------------- Metrics ----------------

    /// <summary>Latest CPU sample from the scheduler monitor ring buffer (one record per minute).</summary>
    public const string CpuRingBuffer = """
        SELECT TOP (1)
            x.record.value('(./Record/SchedulerMonitorEvent/SystemHealth/ProcessUtilization)[1]', 'int') AS SqlCpu,
            x.record.value('(./Record/SchedulerMonitorEvent/SystemHealth/SystemIdle)[1]', 'int')         AS SystemIdle
        FROM (
            SELECT [timestamp], CONVERT(xml, record) AS record
            FROM sys.dm_os_ring_buffers
            WHERE ring_buffer_type = N'RING_BUFFER_SCHEDULER_MONITOR' AND record LIKE N'%<SystemHealth>%'
        ) AS x
        ORDER BY x.[timestamp] DESC;
        """;

    /// <summary>Azure SQL Database: CPU of the database from resource stats (15-second samples).</summary>
    public const string CpuAzureSqlDb =
        "SELECT TOP (1) CAST(avg_cpu_percent AS float) FROM sys.dm_db_resource_stats ORDER BY end_time DESC;";

    public const string PerfCounters = """
        SELECT RTRIM(counter_name) AS CounterName, CAST(cntr_value AS bigint) AS Value
        FROM sys.dm_os_performance_counters
        WHERE (object_name LIKE N'%Buffer Manager%' AND counter_name = N'Page life expectancy')
           OR (object_name LIKE N'%Memory Manager%' AND counter_name IN (N'Memory Grants Pending', N'Total Server Memory (KB)', N'Target Server Memory (KB)'))
           OR (object_name LIKE N'%SQL Statistics%' AND counter_name = N'Batch Requests/sec')
           OR (object_name LIKE N'%General Statistics%' AND counter_name = N'User Connections');
        """;

    public const string BlockedSessions =
        "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id <> 0;";

    // ---------------- Waits / file I/O (cumulative since restart; the engine computes deltas) ----------------

    public const string WaitStats =
        "SELECT wait_type AS WaitType, wait_time_ms AS WaitTimeMs, signal_wait_time_ms AS SignalWaitMs, waiting_tasks_count AS WaitingTasks FROM sys.dm_os_wait_stats WHERE wait_time_ms > 0;";

    public const string WaitStatsAzureSqlDb =
        "SELECT wait_type AS WaitType, wait_time_ms AS WaitTimeMs, signal_wait_time_ms AS SignalWaitMs, waiting_tasks_count AS WaitingTasks FROM sys.dm_db_wait_stats WHERE wait_time_ms > 0;";

    public const string FileStats = """
        SELECT DB_NAME(vfs.database_id) AS DatabaseName, vfs.database_id AS DatabaseId, vfs.file_id AS FileId,
               vfs.num_of_reads AS Reads, vfs.num_of_writes AS Writes,
               vfs.num_of_bytes_read AS BytesRead, vfs.num_of_bytes_written AS BytesWritten,
               vfs.io_stall_read_ms AS ReadStallMs, vfs.io_stall_write_ms AS WriteStallMs
        FROM sys.dm_io_virtual_file_stats(NULL, NULL) AS vfs;
        """;

    public const string FileStatsAzureSqlDb = """
        SELECT DB_NAME() AS DatabaseName, vfs.database_id AS DatabaseId, vfs.file_id AS FileId,
               vfs.num_of_reads AS Reads, vfs.num_of_writes AS Writes,
               vfs.num_of_bytes_read AS BytesRead, vfs.num_of_bytes_written AS BytesWritten,
               vfs.io_stall_read_ms AS ReadStallMs, vfs.io_stall_write_ms AS WriteStallMs
        FROM sys.dm_io_virtual_file_stats(DB_ID(), NULL) AS vfs;
        """;

    // ---------------- Databases ----------------

    private const string DatabaseColumns = """
        SELECT d.database_id AS DatabaseId, d.name AS Name,
               DATEADD(SECOND, DATEDIFF(SECOND, GETDATE(), GETUTCDATE()), d.create_date) AS CreateDateUtc,
               d.state_desc AS State, d.recovery_model_desc AS RecoveryModel, CAST(d.compatibility_level AS int) AS CompatibilityLevel,
               SUSER_SNAME(d.owner_sid) AS Owner, d.is_read_only AS IsReadOnly, d.is_auto_close_on AS IsAutoCloseOn,
               d.is_auto_shrink_on AS IsAutoShrinkOn, d.log_reuse_wait_desc AS LogReuseWait,
               CAST(ISNULL(HAS_DBACCESS(d.name), 0) AS bit) AS HasAccess,
               CAST(DATABASEPROPERTYEX(d.name, 'LastGoodCheckDbTime') AS datetime) AS LastGoodCheckDbLocal,
               DATEDIFF(SECOND, GETDATE(), GETUTCDATE()) AS UtcOffsetSeconds
        FROM sys.databases AS d
        """;

    public const string Databases = DatabaseColumns + ";";

    public const string DatabasesAzureSqlDb = DatabaseColumns + " WHERE d.database_id = DB_ID();";

    public const string Files = """
        SELECT mf.database_id AS DatabaseId, mf.file_id AS FileId, mf.name AS LogicalName, mf.physical_name AS PhysicalName,
               mf.type_desc AS TypeDesc,
               CAST(mf.size AS float) * 8 / 1024 AS SizeMb,
               CASE WHEN mf.max_size = -1 OR (mf.type = 1 AND mf.max_size = 268435456) THEN NULL
                    ELSE CAST(mf.max_size AS float) * 8 / 1024 END AS MaxSizeMb,
               mf.is_percent_growth AS IsPercentGrowth,
               CASE WHEN mf.is_percent_growth = 1 THEN CAST(mf.growth AS float) ELSE CAST(mf.growth AS float) * 8 / 1024 END AS GrowthValue
        FROM sys.master_files AS mf;
        """;

    public const string FilesAzureSqlDb = """
        SELECT DB_ID() AS DatabaseId, df.file_id AS FileId, df.name AS LogicalName, df.physical_name AS PhysicalName,
               df.type_desc AS TypeDesc,
               CAST(df.size AS float) * 8 / 1024 AS SizeMb,
               CASE WHEN df.max_size = -1 THEN NULL ELSE CAST(df.max_size AS float) * 8 / 1024 END AS MaxSizeMb,
               df.is_percent_growth AS IsPercentGrowth,
               CASE WHEN df.is_percent_growth = 1 THEN CAST(df.growth AS float) ELSE CAST(df.growth AS float) * 8 / 1024 END AS GrowthValue
        FROM sys.database_files AS df;
        """;

    /// <summary>Volume of every file of online databases. Needs VIEW SERVER STATE.</summary>
    public const string Volumes = """
        SELECT mf.database_id AS DatabaseId, mf.file_id AS FileId, vs.volume_mount_point AS MountPoint,
               vs.logical_volume_name AS Label,
               CAST(vs.total_bytes / 1048576 AS bigint) AS TotalMb, CAST(vs.available_bytes / 1048576 AS bigint) AS FreeMb
        FROM sys.master_files AS mf
        JOIN sys.databases AS d ON d.database_id = mf.database_id AND d.state = 0
        CROSS APPLY sys.dm_os_volume_stats(mf.database_id, mf.file_id) AS vs;
        """;

    /// <summary>Run inside each database: used space per file.</summary>
    public const string UsedSpace =
        "SELECT file_id AS FileId, CAST(FILEPROPERTY(name, 'SpaceUsed') AS float) * 8 / 1024 AS UsedMb FROM sys.database_files;";

    /// <summary>Run inside each database: number of virtual log files (SQL Server 2016 SP2+).</summary>
    public const string VlfCount = "SELECT COUNT(*) FROM sys.dm_db_log_info(DB_ID());";

    // ---------------- Backups (msdb) ----------------

    public const string LatestBackups = """
        SELECT bs.database_name AS DatabaseName, bs.type AS BackupType,
               DATEADD(SECOND, DATEDIFF(SECOND, GETDATE(), GETUTCDATE()), MAX(bs.backup_finish_date)) AS LastFinishUtc
        FROM msdb.dbo.backupset AS bs
        WHERE bs.backup_finish_date IS NOT NULL
        GROUP BY bs.database_name, bs.type;
        """;

    public const string BackupHistory = """
        DECLARE @offset int = DATEDIFF(SECOND, GETDATE(), GETUTCDATE());
        SELECT TOP (5000)
               CAST(bs.backup_set_id AS bigint) AS BackupSetId, bs.database_name AS DatabaseName, bs.type AS BackupType,
               DATEADD(SECOND, @offset, bs.backup_start_date) AS StartUtc, DATEADD(SECOND, @offset, bs.backup_finish_date) AS FinishUtc,
               CAST(bs.backup_size AS bigint) AS SizeBytes, CAST(bs.compressed_backup_size AS bigint) AS CompressedBytes,
               bs.is_copy_only AS IsCopyOnly, bs.recovery_model AS RecoveryModel,
               CAST(mf.device_type AS int) AS DeviceType, mf.physical_device_name AS PhysicalDevice
        FROM msdb.dbo.backupset AS bs
        LEFT JOIN msdb.dbo.backupmediafamily AS mf
               ON mf.media_set_id = bs.media_set_id AND mf.family_sequence_number = 1 AND mf.mirror = 0
        WHERE bs.backup_set_id > @after
          AND bs.backup_finish_date IS NOT NULL
          AND bs.backup_finish_date >= DATEADD(DAY, -@days, GETDATE())
        ORDER BY bs.backup_set_id;
        """;

    // ---------------- Configuration ----------------

    public const string Configuration = """
        SELECT name AS Name, CAST(value AS bigint) AS Value, CAST(value_in_use AS bigint) AS ValueInUse,
               CAST(minimum AS bigint) AS Minimum, CAST(maximum AS bigint) AS Maximum,
               is_dynamic AS IsDynamic, is_advanced AS IsAdvanced, CAST(description AS nvarchar(512)) AS Description
        FROM sys.configurations;
        """;
}
