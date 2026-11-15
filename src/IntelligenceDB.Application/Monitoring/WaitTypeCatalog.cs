namespace IntelligenceDB.Application.Monitoring;

public sealed record WaitInfo(string Category, string Explanation);

/// <summary>
/// Plain-English explanations of SQL Server wait types, and the list of "benign" waits that are
/// normal background activity and are filtered out (based on the widely used community list).
/// </summary>
public static class WaitTypeCatalog
{
    // Background / idle waits that do not indicate a problem.
    private static readonly string[] BenignPrefixes =
    [
        "BROKER_", "SLEEP_", "SQLTRACE_", "XE_", "QDS_", "HADR_FILESTREAM_", "HADR_WORK_QUEUE", "HADR_CLUSAPI_CALL",
        "HADR_LOGCAPTURE_WAIT", "HADR_NOTIFICATION_DEQUEUE", "HADR_TIMER_TASK", "PARALLEL_REDO_", "PREEMPTIVE_XE_",
        "PREEMPTIVE_OS_LIBRARYOPS", "PREEMPTIVE_OS_COMOPS", "PREEMPTIVE_OS_CRYPTOPS", "PREEMPTIVE_OS_PIPEOPS",
        "PREEMPTIVE_OS_AUTHENTICATIONOPS", "PREEMPTIVE_OS_GENERICOPS", "PREEMPTIVE_OS_VERIFYTRUST",
        "PREEMPTIVE_OS_FILEOPS", "PREEMPTIVE_OS_DEVICEOPS", "PREEMPTIVE_OS_QUERYREGISTRY", "PREEMPTIVE_OS_WRITEFILE",
        "PREEMPTIVE_OS_DELETESECURITYCONTEXT", "PREEMPTIVE_OS_REPORTEVENT", "PREEMPTIVE_HADR_LEASE_MECHANISM",
        "PREEMPTIVE_SP_SERVER_DIAGNOSTICS", "WAIT_XTP_", "PVS_PREALLOCATE", "AZURE_IMDS_VERSIONS",
    ];

    private static readonly HashSet<string> BenignExact = new(StringComparer.OrdinalIgnoreCase)
    {
        "CHECKPOINT_QUEUE", "CHKPT", "CLR_AUTO_EVENT", "CLR_MANUAL_EVENT", "CLR_SEMAPHORE", "DBMIRROR_DBM_EVENT",
        "DBMIRROR_EVENTS_QUEUE", "DBMIRROR_WORKER_QUEUE", "DBMIRRORING_CMD", "DIRTY_PAGE_POLL",
        "DISPATCHER_QUEUE_SEMAPHORE", "EXECSYNC", "FSAGENT", "FT_IFTS_SCHEDULER_IDLE_WAIT", "FT_IFTSHC_MUTEX",
        "KSOURCE_WAKEUP", "LAZYWRITER_SLEEP", "LOGMGR_QUEUE", "MEMORY_ALLOCATION_EXT", "ONDEMAND_TASK_QUEUE",
        "PWAIT_ALL_COMPONENTS_INITIALIZED", "PWAIT_DIRECTLOGCONSUMER_GETNEXT", "PWAIT_EXTENSIBILITY_CLEANUP_TASK",
        "REDO_THREAD_PENDING_WORK", "REQUEST_FOR_DEADLOCK_SEARCH", "RESOURCE_QUEUE", "SERVER_IDLE_CHECK",
        "SNI_HTTP_ACCEPT", "SOS_WORK_DISPATCHER", "SP_SERVER_DIAGNOSTICS_SLEEP", "STARTUP_DEPENDENCY_MANAGER",
        "UCS_SESSION_REGISTRATION", "VDI_CLIENT_OTHER", "WAIT_FOR_RESULTS", "WAITFOR", "WAITFOR_TASKSHUTDOWN",
        "XTP_PREEMPTIVE_TASK", "HADR_SYNC_COMMIT_IDLE", "SOS_PHYS_PAGE_CACHE",
    };

    private static readonly (string Prefix, WaitInfo Info)[] Known =
    [
        ("CXPACKET", new("Parallelism", "Queries running in parallel wait for their slowest thread. Common on big scans; check MAXDOP and Cost Threshold for Parallelism, and missing indexes.")),
        ("CXCONSUMER", new("Parallelism", "A parallel query's consumer thread waits for producers. Usually harmless; high values follow CXPACKET.")),
        ("CXSYNC_", new("Parallelism", "Parallel query threads synchronising. Same advice as CXPACKET.")),
        ("PAGEIOLATCH_", new("Disk reads", "Waiting to read data pages from disk into memory. Points to slow storage, too little memory, or queries reading far more data than needed (missing indexes).")),
        ("WRITELOG", new("Log disk", "Waiting for transaction log writes to finish. The log disk is slow or there are many tiny transactions. Put the log on fast storage.")),
        ("LCK_M_", new("Blocking", "Waiting for a lock held by another session (blocking). Find the head blocker; keep transactions short; consider RCSI.")),
        ("SOS_SCHEDULER_YIELD", new("CPU", "Threads give up the CPU after their time slice and wait to run again. High values mean CPU pressure or CPU-heavy queries.")),
        ("THREADPOOL", new("Worker threads", "No worker thread was free to run a task - a serious sign of overload or heavy blocking.")),
        ("RESOURCE_SEMAPHORE", new("Memory", "Queries wait for a memory grant to start. Look for queries with huge sorts/hashes, and memory pressure.")),
        ("ASYNC_NETWORK_IO", new("Client / network", "SQL Server waits for the application to read results. Usually the client processes rows slowly or fetches too many rows.")),
        ("PAGELATCH_", new("In-memory contention", "Contention on pages already in memory, often tempdb allocation pages or 'last page insert' hot spots.")),
        ("LATCH_", new("Internal contention", "Waits on internal non-page structures. Investigate with sys.dm_os_latch_stats if high.")),
        ("IO_COMPLETION", new("Disk", "Waiting for non-data-page I/O such as sort spills to tempdb or backup/restore reads.")),
        ("ASYNC_IO_COMPLETION", new("Disk", "Waiting for asynchronous I/O, typically backups, restores or file growth.")),
        ("BACKUPIO", new("Backups", "Backup waiting for backup device I/O.")),
        ("BACKUPBUFFER", new("Backups", "Backup waiting for a free buffer; the backup target is slow.")),
        ("HADR_SYNC_COMMIT", new("Availability Groups", "Commits wait for a synchronous secondary replica to harden the log. Check network and secondary disk speed.")),
        ("OLEDB", new("Linked servers", "Waiting on OLE DB calls (linked servers, some DMVs). Slow remote servers show up here.")),
        ("PREEMPTIVE_", new("External calls", "SQL Server called the operating system or an external component and waits for it.")),
        ("CMEMTHREAD", new("Memory", "Contention on a shared memory object, often from many plan compilations.")),
        ("RESOURCE_GOVERNOR_IDLE", new("Resource Governor", "A Resource Governor pool cap is throttling CPU.")),
        ("SOS_WORKER_MIGRATION", new("CPU", "Worker threads move between schedulers.")),
        ("LOG_RATE_GOVERNOR", new("Azure limits", "Azure SQL is throttling transaction log writes to the service tier's limit.")),
        ("IO_QUEUE_LIMIT", new("Azure limits", "Azure SQL is throttling I/O to the service tier's limit.")),
    ];

    public static bool IsBenign(string waitType)
    {
        ArgumentNullException.ThrowIfNull(waitType);
        if (BenignExact.Contains(waitType)) return true;
        foreach (var p in BenignPrefixes)
        {
            if (waitType.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    public static WaitInfo Describe(string waitType)
    {
        ArgumentNullException.ThrowIfNull(waitType);
        foreach (var (prefix, info) in Known)
        {
            if (waitType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return info;
        }

        return new WaitInfo("Other", "Less common wait type. Search the Microsoft documentation for this name if it is in the top waits.");
    }
}
