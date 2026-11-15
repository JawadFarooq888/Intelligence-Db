using IntelligenceDB.Domain.Servers;

namespace IntelligenceDB.Application.Capabilities;

/// <summary>
/// Decides, for one server, which IntelligenceDB features are supported.
/// Pure function of (platform, facts) so it is fully unit-testable.
/// Every "not supported" answer carries a plain-English reason and an alternative.
/// </summary>
public static class CapabilityEvaluator
{
    private const string MonitoringScriptHint =
        "Run the monitoring login script for this platform (Server page → Login scripts).";

    private const string AdminScriptHint =
        "Add an admin login for this server (see the admin login script) to enable control actions.";

    public static IReadOnlyList<CapabilityResult> Evaluate(
        PlatformType platform,
        ProbeFacts facts,
        AdminProbeOutcome admin)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(admin);

        bool sqlDb = platform == PlatformType.AzureSqlDatabase;
        var results = new List<CapabilityResult>();

        // ---- Monitoring capabilities (monitoring login) ----

        results.Add(facts.HasViewServerState
            ? Ok(CapabilityKeys.ServerStateDmvs)
            : No(CapabilityKeys.ServerStateDmvs,
                sqlDb
                    ? "The monitoring user does not have VIEW DATABASE STATE, so performance views (waits, sessions, CPU) are hidden."
                    : "The monitoring login does not have VIEW SERVER STATE, so performance views (waits, sessions, CPU) only show its own activity.",
                MonitoringScriptHint));

        results.Add(facts.HasViewAnyDefinition
            ? Ok(CapabilityKeys.MetadataVisibility)
            : No(CapabilityKeys.MetadataVisibility,
                sqlDb
                    ? "The monitoring user does not have VIEW DEFINITION, so index and object details may be incomplete."
                    : "The monitoring login does not have VIEW ANY DEFINITION, so index and object details may be incomplete.",
                MonitoringScriptHint));

        var msdb = EvaluateMsdb(sqlDb, facts);
        results.Add(msdb);

        var agent = EvaluateAgent(sqlDb, facts, msdb);
        results.Add(agent);

        results.Add(EvaluateBackupHistory(sqlDb, msdb));
        results.Add(EvaluateVolumes(sqlDb, facts));
        results.Add(EvaluateErrorLog(platform, facts));
        results.Add(EvaluateSystemHealth(sqlDb, facts));
        results.Add(EvaluateQueryStore(platform, facts));
        results.Add(EvaluateOnlineIndex(facts));

        // ---- Control capabilities (admin login) ----

        string? adminProblem = !admin.Configured
            ? "No admin login is configured for this server."
            : admin.Facts is null
                ? $"The admin login could not connect: {admin.Error ?? "unknown error"}"
                : null;

        results.Add(EvaluateKill(sqlDb, admin.Facts, adminProblem));
        results.Add(EvaluateJobControl(agent, admin.Facts, adminProblem));
        results.Add(EvaluateConfigChange(platform, admin.Facts, adminProblem));
        results.Add(adminProblem is null
            ? Ok(CapabilityKeys.IndexMaintenance, "Permission is checked for each database when an action runs.")
            : No(CapabilityKeys.IndexMaintenance, adminProblem, AdminScriptHint));

        return results;
    }

    private static CapabilityResult EvaluateMsdb(bool sqlDb, ProbeFacts f)
    {
        if (sqlDb)
        {
            return No(CapabilityKeys.MsdbAccess,
                "Azure SQL Database has no msdb database.",
                "Backups are managed by Azure; IntelligenceDB's own scheduler replaces SQL Agent jobs.");
        }

        return f.HasMsdbAccess
            ? Ok(CapabilityKeys.MsdbAccess)
            : No(CapabilityKeys.MsdbAccess,
                "The monitoring login cannot read msdb (job and backup history).",
                "Add the login to the msdb roles SQLAgentReaderRole and db_datareader (included in the monitoring login script).");
    }

    private static CapabilityResult EvaluateAgent(bool sqlDb, ProbeFacts f, CapabilityResult msdb)
    {
        if (sqlDb)
        {
            return No(CapabilityKeys.SqlAgent,
                "Azure SQL Database has no SQL Server Agent.",
                "Use IntelligenceDB's built-in scheduler or Azure Elastic Jobs.");
        }

        if (f.Edition.Contains("Express", StringComparison.OrdinalIgnoreCase))
        {
            return No(CapabilityKeys.SqlAgent,
                "SQL Server Express edition does not include SQL Server Agent.",
                "Use IntelligenceDB's built-in scheduler, or Windows Task Scheduler with sqlcmd.");
        }

        if (!msdb.IsSupported)
        {
            return No(CapabilityKeys.SqlAgent,
                "SQL Agent jobs are stored in msdb, which this login cannot read.",
                msdb.Alternative);
        }

        if (f.IsAgentRunning == false)
        {
            return No(CapabilityKeys.SqlAgent,
                "The SQL Server Agent service is not running.",
                "Start SQL Server Agent (on Linux: mssql-conf set sqlagent.enabled true, or MSSQL_AGENT_ENABLED=true in Docker).");
        }

        return Ok(CapabilityKeys.SqlAgent,
            f.IsAgentRunning is null ? "Agent service state could not be confirmed (needs VIEW SERVER STATE)." : null);
    }

    private static CapabilityResult EvaluateBackupHistory(bool sqlDb, CapabilityResult msdb)
    {
        if (sqlDb)
        {
            return No(CapabilityKeys.BackupHistory,
                "Azure SQL Database backups are automatic and are not recorded in msdb.",
                "Check backup retention and point-in-time restore in the Azure portal.");
        }

        return msdb.IsSupported
            ? Ok(CapabilityKeys.BackupHistory)
            : No(CapabilityKeys.BackupHistory, "Backup history is stored in msdb, which this login cannot read.", msdb.Alternative);
    }

    private static CapabilityResult EvaluateVolumes(bool sqlDb, ProbeFacts f)
    {
        if (sqlDb)
        {
            return No(CapabilityKeys.VolumeStats,
                "Azure SQL Database does not expose disk volumes.",
                "Use the database's used size versus its max size instead.");
        }

        return f.CanReadVolumeStats
            ? Ok(CapabilityKeys.VolumeStats)
            : No(CapabilityKeys.VolumeStats,
                "Disk free space (sys.dm_os_volume_stats) is not readable by this login.",
                "Grant VIEW SERVER STATE to the monitoring login.");
    }

    private static CapabilityResult EvaluateErrorLog(PlatformType platform, ProbeFacts f)
    {
        if (platform == PlatformType.AzureSqlDatabase)
        {
            return No(CapabilityKeys.ErrorLog,
                "Azure SQL Database has no SQL Server error log.",
                "Use Azure diagnostic settings and auditing.");
        }

        if (f.CanReadErrorLog) return Ok(CapabilityKeys.ErrorLog);

        return platform == PlatformType.AwsRds
            ? No(CapabilityKeys.ErrorLog,
                "Reading the error log on RDS needs EXECUTE on rdsadmin.dbo.rds_read_error_log.",
                "Download error logs from the RDS console, or grant that procedure to the monitoring login.")
            : No(CapabilityKeys.ErrorLog,
                "Reading the SQL Server error log requires the securityadmin or sysadmin role.",
                "Optional: add the monitoring login to securityadmin, or read the log in SSMS.");
    }

    private static CapabilityResult EvaluateSystemHealth(bool sqlDb, ProbeFacts f)
    {
        if (sqlDb)
        {
            return No(CapabilityKeys.SystemHealthSession,
                "Azure SQL Database has no server-level system_health session.",
                "Create a database-scoped Extended Events session to capture deadlocks.");
        }

        return f.HasViewServerState
            ? Ok(CapabilityKeys.SystemHealthSession)
            : No(CapabilityKeys.SystemHealthSession,
                "Deadlock history from the system_health session needs VIEW SERVER STATE.",
                MonitoringScriptHint);
    }

    private static CapabilityResult EvaluateQueryStore(PlatformType platform, ProbeFacts f)
    {
        // Query Store exists from SQL Server 2016 (v13). Azure platforms always have it.
        bool available = platform is PlatformType.AzureSqlDatabase or PlatformType.AzureSqlManagedInstance
                         || f.MajorVersion >= 13;

        return available
            ? Ok(CapabilityKeys.QueryStore, "Query Store must also be turned on in each database.")
            : No(CapabilityKeys.QueryStore,
                "Query Store requires SQL Server 2016 or newer.",
                "Plan cache statistics are used instead.");
    }

    private static CapabilityResult EvaluateOnlineIndex(ProbeFacts f)
    {
        // Enterprise/Developer/Evaluation (3), Azure SQL Database (5) and Managed Instance (8).
        return f.EngineEdition is 3 or 5 or 8
            ? Ok(CapabilityKeys.OnlineIndexOperations)
            : No(CapabilityKeys.OnlineIndexOperations,
                "Online index rebuild requires Enterprise or Developer edition.",
                "Rebuild offline inside the maintenance window, or use REORGANIZE (always online).");
    }

    private static CapabilityResult EvaluateKill(bool sqlDb, AdminFacts? a, string? adminProblem)
    {
        if (adminProblem is not null || a is null)
        {
            return No(CapabilityKeys.KillSession, adminProblem ?? "No admin login.", AdminScriptHint);
        }

        return a.HasAlterAnyConnection
            ? Ok(CapabilityKeys.KillSession)
            : No(CapabilityKeys.KillSession,
                sqlDb
                    ? "The admin user lacks KILL DATABASE CONNECTION."
                    : "The admin login lacks ALTER ANY CONNECTION (needed for KILL).",
                "Grant the permission listed in the admin login script.");
    }

    private static CapabilityResult EvaluateJobControl(CapabilityResult agent, AdminFacts? a, string? adminProblem)
    {
        if (!agent.IsSupported)
        {
            return No(CapabilityKeys.AgentJobControl, agent.Reason, agent.Alternative);
        }

        if (adminProblem is not null || a is null)
        {
            return No(CapabilityKeys.AgentJobControl, adminProblem ?? "No admin login.", AdminScriptHint);
        }

        return a.IsSysadmin || a.CanControlAgentJobs
            ? Ok(CapabilityKeys.AgentJobControl)
            : No(CapabilityKeys.AgentJobControl,
                "The admin login is not a member of the msdb role SQLAgentOperatorRole (or sysadmin).",
                "Add the admin login to SQLAgentOperatorRole in msdb.");
    }

    private static CapabilityResult EvaluateConfigChange(PlatformType platform, AdminFacts? a, string? adminProblem)
    {
        switch (platform)
        {
            case PlatformType.AzureSqlDatabase:
                return No(CapabilityKeys.ServerConfigurationChange,
                    "Azure SQL Database has no instance-level settings (sp_configure).",
                    "Use ALTER DATABASE SCOPED CONFIGURATION.");
            case PlatformType.AwsRds:
                return No(CapabilityKeys.ServerConfigurationChange,
                    "Amazon RDS manages instance settings through DB parameter groups.",
                    "Change the setting in the RDS parameter group.");
            case PlatformType.GoogleCloudSql:
                return No(CapabilityKeys.ServerConfigurationChange,
                    "Cloud SQL manages instance settings through database flags.",
                    "Change the setting with Cloud SQL database flags.");
        }

        if (adminProblem is not null || a is null)
        {
            return No(CapabilityKeys.ServerConfigurationChange, adminProblem ?? "No admin login.", AdminScriptHint);
        }

        return a.IsSysadmin || a.HasAlterSettings
            ? Ok(CapabilityKeys.ServerConfigurationChange)
            : No(CapabilityKeys.ServerConfigurationChange,
                "The admin login lacks ALTER SETTINGS.",
                "Grant ALTER SETTINGS to the admin login (see admin login script).");
    }

    private static CapabilityResult Ok(string key, string? note = null) => new(key, true, note, null);

    private static CapabilityResult No(string key, string? reason, string? alternative) =>
        new(key, false, reason, alternative);
}
