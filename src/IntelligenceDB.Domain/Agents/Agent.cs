namespace IntelligenceDB.Domain.Agents;

/// <summary>
/// An IntelligenceDB Agent: a small service inside a customer network that monitors the SQL Servers
/// the portal cannot reach directly. It always connects OUT to the portal over HTTPS.
/// </summary>
public class Agent
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>SHA-256 (hex) of the agent key. The key itself is shown once and never stored.</summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>First characters of the key, so admins can tell keys apart.</summary>
    public string KeyPrefix { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
    public DateTime? LastSeenUtc { get; set; }
    public string? Version { get; set; }
    public string? MachineName { get; set; }
    public string? LastIp { get; set; }
}

/// <summary>
/// Work the portal asks an agent to do right now (e.g. "test this connection").
/// The agent polls for commands; the payload may contain secrets and is stored encrypted.
/// </summary>
public class AgentCommand
{
    public long Id { get; set; }
    public Guid AgentId { get; set; }
    public string Type { get; set; } = AgentCommandTypes.Probe;
    public byte[]? ProtectedPayload { get; set; }
    public string Status { get; set; } = AgentCommandStatus.Pending;
    public string? ResultJson { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? DeliveredUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
}

public static class AgentCommandTypes
{
    /// <summary>Test a connection and detect capabilities.</summary>
    public const string Probe = "Probe";
}

public static class AgentCommandStatus
{
    public const string Pending = "Pending";
    public const string Delivered = "Delivered";
    public const string Completed = "Completed";
    public const string Expired = "Expired";
}
