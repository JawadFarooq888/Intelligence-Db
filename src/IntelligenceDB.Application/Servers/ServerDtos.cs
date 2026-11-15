using IntelligenceDB.Domain.Servers;

namespace IntelligenceDB.Application.Servers;

/// <summary>Login details sent by the UI. <see cref="Secret"/> is write-only: empty means "keep the stored one".</summary>
public sealed class CredentialInput
{
    public AuthType AuthType { get; init; } = AuthType.SqlLogin;
    public string? UserName { get; init; }
    public string? Secret { get; init; }
    public string? TenantId { get; init; }
    public string? ClientId { get; init; }
}

public sealed class ServerUpsertRequest
{
    public string DisplayName { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public int? Port { get; init; }
    public string? InstanceName { get; init; }
    public string? DatabaseName { get; init; }
    public PlatformType DeclaredPlatform { get; init; } = PlatformType.Unknown;
    public ServerEnvironment Environment { get; init; } = ServerEnvironment.Production;
    public int? GroupId { get; init; }

    /// <summary>Agent that reaches the server from inside its network; null = connect directly from the portal.</summary>
    public Guid? AgentId { get; init; }

    public List<string> Tags { get; init; } = [];
    public bool IsEnabled { get; init; } = true;
    public bool Encrypt { get; init; } = true;
    public bool TrustServerCertificate { get; init; }
    public int ConnectTimeoutSeconds { get; init; } = 15;
    public int CollectionIntervalSeconds { get; init; } = 60;
    public CredentialInput Monitoring { get; init; } = new();

    /// <summary>Optional admin login. Null on update means "leave as is" unless <see cref="RemoveAdmin"/> is true.</summary>
    public CredentialInput? Admin { get; init; }

    public bool RemoveAdmin { get; init; }

    /// <summary>Base64 row version from the last read, for optimistic concurrency on update.</summary>
    public string? RowVersion { get; init; }
}

/// <summary>Credential as shown to the UI. Never contains the secret.</summary>
public sealed record CredentialDto(AuthType AuthType, string? UserName, bool HasSecret, string? TenantId, string? ClientId);

public sealed record CapabilityDto(string Key, string Title, string Description, bool IsSupported, string? Reason, string? Alternative);

public sealed record ServerSummaryDto(
    int Id,
    string DisplayName,
    string Host,
    int? Port,
    string? InstanceName,
    string? DatabaseName,
    PlatformType DeclaredPlatform,
    PlatformType DetectedPlatform,
    PlatformType EffectivePlatform,
    ServerEnvironment Environment,
    int? GroupId,
    string? GroupName,
    IReadOnlyList<string> Tags,
    bool IsEnabled,
    ConnectionStatus LastStatus,
    DateTime? LastCheckedUtc,
    string? LastError,
    string? VersionLabel,
    string? Edition,
    bool HasAdminLogin,
    Guid? AgentId,
    string? AgentName,
    int? HealthScore);

public sealed record ServerInfoDto(
    string ProductVersion,
    int MajorVersion,
    string? ProductLevel,
    string? ProductUpdateLevel,
    string VersionLabel,
    string Edition,
    int EngineEdition,
    string? HostPlatform,
    int? CpuCount,
    long? PhysicalMemoryMb,
    DateTime? SqlStartTimeUtc,
    string? Collation,
    string? ServerName,
    bool IsSysadmin,
    DateTime DetectedUtc);

public sealed record ServerDetailDto(
    ServerSummaryDto Server,
    bool Encrypt,
    bool TrustServerCertificate,
    int ConnectTimeoutSeconds,
    int CollectionIntervalSeconds,
    CredentialDto Monitoring,
    CredentialDto? Admin,
    ServerInfoDto? Info,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<CapabilityDto> Capabilities,
    string? PlatformWarning,
    string RowVersion);

public sealed record TestConnectionResultDto(
    bool Success,
    ConnectionStatus Status,
    string? Error,
    int ElapsedMs,
    PlatformType? DetectedPlatform,
    string? PlatformWarning,
    string? VersionLabel,
    string? Edition,
    bool? IsSysadmin,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<CapabilityDto> Capabilities,
    bool AdminTested,
    bool AdminSuccess,
    string? AdminError);
