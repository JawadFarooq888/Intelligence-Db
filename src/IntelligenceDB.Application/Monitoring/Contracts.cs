using System.Text.Json;
using System.Text.Json.Serialization;
using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Domain.Servers;

namespace IntelligenceDB.Application.Monitoring;

// Data contracts shared by the collection engine (Worker and Agent) and the ingestion side (Worker and API).
// Agents send these as JSON over HTTPS, so they must stay plain, serialisable data.

public static class CollectorNames
{
    public const string Connection = "connection";
    public const string Probe = "probe";
    public const string Metrics = "metrics";
    public const string Waits = "waits";
    public const string FileIo = "fileio";
    public const string Databases = "databases";
    public const string Backups = "backups";
    public const string Config = "config";
}

/// <summary>Result of running one collector once against one server.</summary>
public sealed class CollectionBatch
{
    public int ServerId { get; set; }
    public string Collector { get; set; } = string.Empty;
    public DateTime StartedUtc { get; set; }
    public int DurationMs { get; set; }
    public bool Success { get; set; }

    /// <summary>Set when the server could not be reached or the login failed.</summary>
    public ConnectionStatus? ConnectionStatus { get; set; }

    public string? Error { get; set; }

    public MetricSample? Metric { get; set; }
    public List<WaitSample>? Waits { get; set; }
    public List<FileIoSample>? FileIo { get; set; }
    public List<VolumeSample>? Volumes { get; set; }
    public DatabaseSnapshot? Databases { get; set; }
    public BackupSnapshot? Backups { get; set; }
    public List<ConfigSample>? Config { get; set; }
    public ProbeResult? Probe { get; set; }
}

public sealed class MetricSample
{
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

public sealed record WaitSample(string WaitType, long WaitTimeMs, long SignalWaitMs, long WaitingTasks, int IntervalSeconds);

public sealed record FileIoSample(
    string DatabaseName, int FileId, long Reads, long Writes, long BytesRead, long BytesWritten,
    long ReadStallMs, long WriteStallMs, int IntervalSeconds);

public sealed record VolumeSample(string MountPoint, string? Label, long TotalMb, long FreeMb);

public sealed class DatabaseSnapshot
{
    public List<DatabaseSample> Databases { get; set; } = [];
    public List<DatabaseFileSample> Files { get; set; } = [];
}

public sealed class DatabaseSample
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
    public int? VlfCount { get; set; }
    public DateTime? LastGoodCheckDbUtc { get; set; }
}

public sealed class DatabaseFileSample
{
    public int DatabaseId { get; set; }
    public int FileId { get; set; }
    public string LogicalName { get; set; } = string.Empty;
    public string? PhysicalName { get; set; }
    public string TypeDesc { get; set; } = string.Empty;
    public double SizeMb { get; set; }
    public double? UsedMb { get; set; }
    public double? MaxSizeMb { get; set; }
    public bool IsPercentGrowth { get; set; }
    public double GrowthValue { get; set; }
    public string? VolumeMountPoint { get; set; }
}

public sealed class BackupSnapshot
{
    /// <summary>Most recent backup per database and type (from all of msdb history).</summary>
    public List<LatestBackupSample> Latest { get; set; } = [];

    /// <summary>New backup rows since the last collection.</summary>
    public List<BackupSample> History { get; set; } = [];
}

public sealed record LatestBackupSample(string DatabaseName, string BackupType, DateTime LastFinishUtc);

public sealed record BackupSample(
    long BackupSetId, string DatabaseName, string BackupType, DateTime StartUtc, DateTime FinishUtc,
    long SizeBytes, long? CompressedBytes, bool IsCopyOnly, string? RecoveryModel, int? DeviceType, string? PhysicalDevice);

public sealed record ConfigSample(
    string Name, long Value, long ValueInUse, long Minimum, long Maximum, bool IsDynamic, bool IsAdvanced, string? Description);

/// <summary>What one server's collection needs: connection details, platform and supported features.</summary>
public sealed class CollectionTarget
{
    public int ServerId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public TargetConnectionSpec Monitoring { get; set; } = null!;
    public TargetConnectionSpec? Admin { get; set; }
    public PlatformType Platform { get; set; }

    /// <summary>Capability keys that are supported on this server (from the last probe).</summary>
    public HashSet<string> SupportedCapabilities { get; set; } = new(StringComparer.Ordinal);

    public int MetricIntervalSeconds { get; set; } = 60;

    /// <summary>Changes whenever the server's settings change, so collectors restart cleanly.</summary>
    public string Fingerprint { get; set; } = string.Empty;
}

/// <summary>JSON settings used on both ends of the agent channel.</summary>
public static class WireJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }
}
