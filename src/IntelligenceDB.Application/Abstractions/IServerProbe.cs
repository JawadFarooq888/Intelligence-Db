using IntelligenceDB.Application.Capabilities;
using IntelligenceDB.Domain.Servers;

namespace IntelligenceDB.Application.Abstractions;

/// <summary>
/// Connects to a target server, reads its version/edition and the login's permissions.
/// Implementations must never throw for connection or permission problems; they return a failed result instead.
/// </summary>
public interface IServerProbe
{
    Task<ProbeResult> ProbeAsync(
        TargetConnectionSpec monitoring,
        TargetConnectionSpec? admin,
        CancellationToken cancellationToken);
}

/// <summary>
/// Everything needed to open a connection to a target server, with the secret already decrypted.
/// This is a class (not a record) so the generated ToString can never print the secret.
/// </summary>
public sealed class TargetConnectionSpec
{
    public required string Host { get; init; }
    public int? Port { get; init; }
    public string? InstanceName { get; init; }
    public string? DatabaseName { get; init; }
    public required AuthType AuthType { get; init; }
    public string? UserName { get; init; }
    public string? Secret { get; init; }
    public string? TenantId { get; init; }
    public string? ClientId { get; init; }
    public bool Encrypt { get; init; } = true;
    public bool TrustServerCertificate { get; init; }
    public int ConnectTimeoutSeconds { get; init; } = 15;
    public PlatformType Platform { get; init; }

    public override string ToString() =>
        $"{Host}{(InstanceName is null ? "" : "\\" + InstanceName)}{(Port is null ? "" : "," + Port)} ({AuthType}, user {UserName ?? "-"})";
}

public sealed record ServerInfoSnapshot(
    string ProductVersion,
    int MajorVersion,
    string? ProductLevel,
    string? ProductUpdateLevel,
    string Edition,
    int EngineEdition,
    string? HostPlatform,
    int? CpuCount,
    long? PhysicalMemoryMb,
    DateTime? SqlStartTimeUtc,
    string? Collation,
    string? ServerName,
    bool IsSysadmin);

public sealed record ProbeResult(
    bool Success,
    ConnectionStatus Status,
    string? Error,
    int ElapsedMs,
    ProbeFacts? Facts,
    ServerInfoSnapshot? Info,
    IReadOnlyList<string> Permissions,
    bool AdminTested,
    AdminFacts? AdminFacts,
    string? AdminError)
{
    public static ProbeResult Failed(ConnectionStatus status, string error, int elapsedMs) =>
        new(false, status, error, elapsedMs, null, null, [], false, null, null);
}
