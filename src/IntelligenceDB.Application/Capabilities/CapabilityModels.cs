namespace IntelligenceDB.Application.Capabilities;

/// <summary>Raw facts read with the monitoring login. Input to platform and capability detection.</summary>
public sealed record ProbeFacts(
    int EngineEdition,
    int MajorVersion,
    string Edition,
    bool HasRdsAdminDatabase,
    bool HasCloudSqlMarker,
    bool IsSysadmin,
    bool HasViewServerState,
    bool HasViewAnyDefinition,
    bool HasMsdbAccess,
    bool? IsAgentRunning,
    bool CanReadVolumeStats,
    bool CanReadErrorLog);

/// <summary>Raw facts read with the optional admin login.</summary>
public sealed record AdminFacts(
    bool IsSysadmin,
    bool HasAlterAnyConnection,
    bool HasAlterSettings,
    bool CanControlAgentJobs);

/// <summary>Result of trying the admin login (if one is configured).</summary>
public sealed record AdminProbeOutcome(bool Configured, AdminFacts? Facts, string? Error)
{
    public static readonly AdminProbeOutcome NotConfigured = new(false, null, null);
}

public sealed record CapabilityResult(string Key, bool IsSupported, string? Reason, string? Alternative);

/// <summary>Stable keys for every capability. The UI uses them to enable/disable features.</summary>
public static class CapabilityKeys
{
    public const string ServerStateDmvs = "ServerStateDmvs";
    public const string MetadataVisibility = "MetadataVisibility";
    public const string MsdbAccess = "MsdbAccess";
    public const string SqlAgent = "SqlAgent";
    public const string BackupHistory = "BackupHistory";
    public const string VolumeStats = "VolumeStats";
    public const string ErrorLog = "ErrorLog";
    public const string SystemHealthSession = "SystemHealthSession";
    public const string QueryStore = "QueryStore";
    public const string OnlineIndexOperations = "OnlineIndexOperations";
    public const string KillSession = "KillSession";
    public const string AgentJobControl = "AgentJobControl";
    public const string ServerConfigurationChange = "ServerConfigurationChange";
    public const string IndexMaintenance = "IndexMaintenance";

    /// <summary>Plain-English title and description for each key (shown in the UI).</summary>
    public static readonly IReadOnlyDictionary<string, (string Title, string Description)> Catalog =
        new Dictionary<string, (string, string)>
        {
            [ServerStateDmvs] = ("Performance views", "Waits, sessions, CPU and memory data from dynamic management views."),
            [MetadataVisibility] = ("Object metadata", "Ability to see index, table and object definitions."),
            [MsdbAccess] = ("msdb access", "Reading job and backup history stored in the msdb database."),
            [SqlAgent] = ("SQL Server Agent", "Viewing Agent jobs, schedules and job history."),
            [BackupHistory] = ("Backup history", "Last full / differential / log backup per database."),
            [VolumeStats] = ("Disk free space", "Free space of the volumes that hold database files."),
            [ErrorLog] = ("Error log", "Reading the SQL Server error log."),
            [SystemHealthSession] = ("Deadlock history", "Deadlocks captured by the built-in system_health session."),
            [QueryStore] = ("Query Store", "Query history and plan regression detection."),
            [OnlineIndexOperations] = ("Online index rebuild", "Rebuilding indexes without blocking users."),
            [KillSession] = ("Kill session", "Ending a user session (admin login, always with confirmation)."),
            [AgentJobControl] = ("Job control", "Start, stop, enable and disable Agent jobs (admin login)."),
            [ServerConfigurationChange] = ("Instance settings change", "Changing sp_configure settings (admin login, always with approval)."),
            [IndexMaintenance] = ("Index maintenance", "Rebuild / reorganize indexes and update statistics (admin login)."),
        };
}
