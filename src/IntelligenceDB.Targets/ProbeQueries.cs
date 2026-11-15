namespace IntelligenceDB.Targets;

/// <summary>
/// T-SQL used by the connection test / capability detection. Every query is tiny, touches only
/// system metadata, and is run separately so that a missing object or permission only disables
/// one capability instead of failing the whole test.
/// </summary>
internal static class ProbeQueries
{
    /// <summary>Works on every supported platform and with any login that can connect.</summary>
    public const string Basics = """
        SELECT
            CAST(SERVERPROPERTY('EngineEdition') AS int)                AS EngineEdition,
            CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128))     AS ProductVersion,
            CAST(SERVERPROPERTY('ProductLevel') AS nvarchar(128))       AS ProductLevel,
            CAST(SERVERPROPERTY('ProductUpdateLevel') AS nvarchar(128)) AS ProductUpdateLevel,
            CAST(SERVERPROPERTY('Edition') AS nvarchar(128))            AS Edition,
            CAST(SERVERPROPERTY('Collation') AS nvarchar(128))          AS Collation,
            CAST(@@SERVERNAME AS nvarchar(256))                         AS ServerName,
            CAST(ISNULL(IS_SRVROLEMEMBER(N'sysadmin'), 0) AS bit)       AS IsSysadmin,
            CAST(ISNULL(IS_SRVROLEMEMBER(N'securityadmin'), 0) AS bit)  AS IsSecurityAdmin;
        """;

    /// <summary>Box product and Managed Instance. Also looks for cloud vendor markers.</summary>
    public const string ServerPermissions = """
        SELECT
            CAST(ISNULL(HAS_PERMS_BY_NAME(NULL, NULL, N'VIEW SERVER STATE'), 0) AS bit)   AS ViewState,
            CAST(ISNULL(HAS_PERMS_BY_NAME(NULL, NULL, N'VIEW ANY DEFINITION'), 0) AS bit) AS ViewDefinition,
            CAST(CASE WHEN DB_ID(N'rdsadmin') IS NOT NULL THEN 1 ELSE 0 END AS bit)      AS HasRdsAdmin,
            CAST(CASE WHEN EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'CustomerDbRootRole')
                      THEN 1 ELSE 0 END AS bit)                                          AS HasCloudSqlMarker;
        """;

    /// <summary>Azure SQL Database: permissions are database-scoped.</summary>
    public const string DatabasePermissions = """
        SELECT
            CAST(ISNULL(HAS_PERMS_BY_NAME(NULL, N'DATABASE', N'VIEW DATABASE STATE'), 0) AS bit) AS ViewState,
            CAST(ISNULL(HAS_PERMS_BY_NAME(NULL, N'DATABASE', N'VIEW DEFINITION'), 0) AS bit)     AS ViewDefinition,
            CAST(0 AS bit)                                                                       AS HasRdsAdmin,
            CAST(0 AS bit)                                                                       AS HasCloudSqlMarker;
        """;

    // TOP (0) returns nothing but still makes SQL Server check access to the object.
    public const string MsdbJobsAccess = "SELECT TOP (0) job_id FROM msdb.dbo.sysjobs;";

    public const string MsdbBackupAccess = "SELECT TOP (0) backup_set_id FROM msdb.dbo.backupset;";

    /// <summary>SQL Agent keeps sessions named "SQLAgent - ..." while it runs. Needs VIEW SERVER STATE.</summary>
    public const string AgentRunning = """
        SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM sys.dm_exec_sessions WHERE program_name LIKE N'SQLAgent - %')
                         THEN 1 ELSE 0 END AS bit);
        """;

    public const string VolumeStats = """
        SELECT TOP (1) v.volume_mount_point
        FROM sys.master_files AS f
        CROSS APPLY sys.dm_os_volume_stats(f.database_id, f.file_id) AS v;
        """;

    public const string RdsErrorLogAccess =
        "SELECT CAST(ISNULL(HAS_PERMS_BY_NAME(N'rdsadmin.dbo.rds_read_error_log', N'OBJECT', N'EXECUTE'), 0) AS bit);";

    /// <summary>sqlserver_start_time is server-local time; converted to UTC with the server's current offset.</summary>
    public const string SysInfo = """
        SELECT
            cpu_count                                                                  AS CpuCount,
            CAST(physical_memory_kb / 1024 AS bigint)                                  AS PhysicalMemoryMb,
            DATEADD(SECOND, DATEDIFF(SECOND, GETDATE(), GETUTCDATE()), sqlserver_start_time) AS SqlStartTimeUtc
        FROM sys.dm_os_sys_info;
        """;

    public const string HostInfo = "SELECT TOP (1) CAST(host_platform AS nvarchar(256)) FROM sys.dm_os_host_info;";

    public const string MyServerPermissions =
        "SELECT DISTINCT CAST(permission_name AS nvarchar(128)) AS permission_name FROM fn_my_permissions(NULL, N'SERVER') ORDER BY permission_name;";

    public const string MyDatabasePermissions =
        "SELECT DISTINCT CAST(permission_name AS nvarchar(128)) AS permission_name FROM fn_my_permissions(NULL, N'DATABASE') ORDER BY permission_name;";

    // ---- Admin login ----

    public const string AdminServer = """
        SELECT
            CAST(ISNULL(IS_SRVROLEMEMBER(N'sysadmin'), 0) AS bit)                          AS IsSysadmin,
            CAST(ISNULL(HAS_PERMS_BY_NAME(NULL, NULL, N'ALTER ANY CONNECTION'), 0) AS bit) AS AlterAnyConnection,
            CAST(ISNULL(HAS_PERMS_BY_NAME(NULL, NULL, N'ALTER SETTINGS'), 0) AS bit)       AS AlterSettings;
        """;

    public const string AdminDatabase = """
        SELECT
            CAST(0 AS bit)                                                                                AS IsSysadmin,
            CAST(ISNULL(HAS_PERMS_BY_NAME(NULL, N'DATABASE', N'KILL DATABASE CONNECTION'), 0) AS bit)     AS AlterAnyConnection,
            CAST(0 AS bit)                                                                                AS AlterSettings;
        """;

    /// <summary>Is the current login (directly) a member of msdb's SQLAgentOperatorRole?</summary>
    public const string AgentOperatorMember = """
        SELECT CAST(CASE WHEN EXISTS (
            SELECT 1
            FROM msdb.sys.database_role_members AS rm
            JOIN msdb.sys.database_principals AS r ON r.principal_id = rm.role_principal_id
            JOIN msdb.sys.database_principals AS u ON u.principal_id = rm.member_principal_id
            WHERE r.name = N'SQLAgentOperatorRole' AND u.sid = SUSER_SID()
        ) THEN 1 ELSE 0 END AS bit);
        """;
}
