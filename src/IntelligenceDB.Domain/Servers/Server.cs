namespace IntelligenceDB.Domain.Servers;

/// <summary>A monitored SQL Server instance (or Azure SQL database).</summary>
public class Server
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int? Port { get; set; }
    public string? InstanceName { get; set; }

    /// <summary>Database to connect to. Required for Azure SQL Database, otherwise defaults to master.</summary>
    public string? DatabaseName { get; set; }

    /// <summary>Platform chosen by the user when adding the server.</summary>
    public PlatformType DeclaredPlatform { get; set; }

    /// <summary>Platform found by capability detection (Unknown until the first successful test).</summary>
    public PlatformType DetectedPlatform { get; set; }

    public ServerEnvironment Environment { get; set; } = ServerEnvironment.Production;

    public int? GroupId { get; set; }
    public ServerGroup? Group { get; set; }

    public int MonitoringCredentialId { get; set; }
    public Credential MonitoringCredential { get; set; } = null!;

    public int? AdminCredentialId { get; set; }
    public Credential? AdminCredential { get; set; }

    public bool IsEnabled { get; set; } = true;
    public bool Encrypt { get; set; } = true;
    public bool TrustServerCertificate { get; set; }
    public int ConnectTimeoutSeconds { get; set; } = 15;

    /// <summary>How often the collector samples this server (seconds).</summary>
    public int CollectionIntervalSeconds { get; set; } = 60;

    public DateTime CreatedUtc { get; set; }
    public DateTime? LastCheckedUtc { get; set; }
    public ConnectionStatus LastStatus { get; set; }
    public string? LastError { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public List<ServerTag> Tags { get; set; } = [];
    public ServerInfo? Info { get; set; }
    public List<ServerCapability> Capabilities { get; set; } = [];

    /// <summary>The platform features are evaluated against: detected if known, otherwise declared.</summary>
    public PlatformType EffectivePlatform =>
        DetectedPlatform != PlatformType.Unknown ? DetectedPlatform : DeclaredPlatform;
}

public class ServerTag
{
    public int ServerId { get; set; }
    public string Tag { get; set; } = string.Empty;
}

public class ServerGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<Server> Servers { get; set; } = [];
}

/// <summary>
/// A login used to reach a target server. The secret is stored only in encrypted form
/// (ASP.NET Data Protection) and is never sent back to the browser.
/// </summary>
public class Credential
{
    public int Id { get; set; }
    public CredentialPurpose Purpose { get; set; }
    public AuthType AuthType { get; set; }

    /// <summary>SQL login name, Entra user name, or (for service principals) the application/client id.</summary>
    public string? UserName { get; set; }

    /// <summary>Encrypted password / client secret. Null for Windows, managed identity and default auth.</summary>
    public byte[]? ProtectedSecret { get; set; }

    public string? TenantId { get; set; }

    /// <summary>Client id of a user-assigned managed identity (optional).</summary>
    public string? ClientId { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime? RotatedUtc { get; set; }
}

/// <summary>Facts read from the server during the last successful connection test.</summary>
public class ServerInfo
{
    public int ServerId { get; set; }
    public string ProductVersion { get; set; } = string.Empty;
    public int MajorVersion { get; set; }
    public string? ProductLevel { get; set; }
    public string? ProductUpdateLevel { get; set; }
    public string Edition { get; set; } = string.Empty;
    public int EngineEdition { get; set; }
    public string? HostPlatform { get; set; }
    public int? CpuCount { get; set; }
    public long? PhysicalMemoryMb { get; set; }
    public DateTime? SqlStartTimeUtc { get; set; }
    public string? Collation { get; set; }
    public string? ServerName { get; set; }
    public bool IsSysadmin { get; set; }

    /// <summary>JSON array of permission names held by the monitoring login.</summary>
    public string PermissionsJson { get; set; } = "[]";

    public DateTime DetectedUtc { get; set; }
}

/// <summary>Whether one feature works on this server, and if not, why and what to do instead.</summary>
public class ServerCapability
{
    public int ServerId { get; set; }
    public string CapabilityKey { get; set; } = string.Empty;
    public bool IsSupported { get; set; }
    public string? Reason { get; set; }
    public string? Alternative { get; set; }
    public DateTime DetectedUtc { get; set; }
}
